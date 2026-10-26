using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using NETUnpaused90.Models;
using OpenAI.Chat;

namespace NETUnpaused90.Services;

public class AiService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiService> _logger;
    private readonly MatchService _matchService;
    private readonly SpeechService _speechService;
    private readonly TokenCredential _credential;
    private readonly IMemoryCache _cache;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AiService(
        IConfiguration configuration,
        ILogger<AiService> logger,
        MatchService matchService,
        SpeechService speechService,
        TokenCredential credential,
        IMemoryCache cache)
    {
        _configuration = configuration;
        _logger = logger;
        _matchService = matchService;
        _speechService = speechService;
        _credential = credential;
        _cache = cache;
    }

    public async Task<CatchUpResponse?> GenerateCatchUpAsync(
        CatchUpRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchService.GetMasterMatchAsync(request.MatchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var langCode = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
        var cacheKey = $"catchup:{request.MatchId}:{request.LastViewedMinute}:{request.CurrentMinute}:{request.ViewMode}:{request.FavoriteTeam}:{request.Length}:{langCode}";
        if (!request.IncludeAudio && _cache.TryGetValue(cacheKey, out CatchUpResponse? cachedCatchUp) && cachedCatchUp is not null)
        {
            return cachedCatchUp with { Voice = _speechService.NextAlternatingVoice() };
        }

        var filteredEvents = match.Timeline
            .Where(e => e.Minute > request.LastViewedMinute && e.Minute <= request.CurrentMinute)
            .OrderBy(e => e.Minute)
            .ToList();

        var filteredInsights = match.GeneratedInsights
            .Where(i => i.Minute > request.LastViewedMinute && i.Minute <= request.CurrentMinute)
            .OrderBy(i => i.Minute)
            .ToList();

        var homeGoalsAtCurrent = match.Timeline.Count(e =>
            e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) &&
            e.Minute <= request.CurrentMinute &&
            (e.Team.Equals(match.HomeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(match.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var awayGoalsAtCurrent = match.Timeline.Count(e =>
            e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) &&
            e.Minute <= request.CurrentMinute &&
            (e.Team.Equals(match.AwayTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(match.AwayTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var (systemPersona, modeGuidelines, temperature) = GetAudiencePersonaProfile(request.ViewMode);
        var (biasSystemAddendum, biasGuidelines) = GetClubBiasProfile(
            request.FavoriteTeam,
            match.HomeTeam.Name,
            match.AwayTeam.Name,
            request.ViewMode);
        var (normalizedLength, lengthGuidelines, maxTokens) = GetSummaryLengthProfile(request.Length, isCatchUp: true);

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write all JSON field values in Mexican Spanish (es-MX), strictly preserving the Audience Persona, Club Bias, and Spoken Narration Length."
            : "Write all JSON field values in British English (en-GB).";

        var prompt = $@"
A viewer stopped watching at minute {request.LastViewedMinute}' and returned at minute {request.CurrentMinute}'.

{modeGuidelines}

{biasGuidelines}

{lengthGuidelines}

VOCAL EMOTION & DELIVERY:
- Write with genuine vocal emotion, dynamic energy, exclamation marks (!) on big moments, and rhetorical questions (?) so the neural commentator voice rises and falls with real match excitement—never flat or robotic!

LANGUAGE: {languageInstruction}

MATCH DATA SLICE ({request.LastViewedMinute}' to {request.CurrentMinute}'):
- Match: {match.HomeTeam.Name} ({match.HomeTeam.Formation}, Coach: {match.HomeTeam.Coach}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}, Coach: {match.AwayTeam.Coach})
- Score at Minute {request.CurrentMinute}': {match.HomeTeam.Name} {homeGoalsAtCurrent} - {awayGoalsAtCurrent} {match.AwayTeam.Name}
- Statistics: {JsonSerializer.Serialize(match.Statistics)}
- Events ({request.LastViewedMinute}' to {request.CurrentMinute}'): {JsonSerializer.Serialize(filteredEvents)}
- Insights ({request.LastViewedMinute}' to {request.CurrentMinute}'): {JsonSerializer.Serialize(filteredInsights)}

Return ONLY a valid JSON object with these exact 4 keys:
{{
  ""whatHappened"": ""..."",
  ""whyItMatters"": ""..."",
  ""momentumChanges"": ""..."",
  ""whatToWatchNext"": ""...""
}}";

        CatchUpResponse? parsed = null;
        var assignedVoice = _speechService.NextAlternatingVoice();

        // 1. Attempt Azure OpenAI with dynamic timeout based on selected length profile
        try
        {
            var llmTimeout = normalizedLength switch
            {
                "full" => TimeSpan.FromSeconds(35),
                "5m" => TimeSpan.FromSeconds(25),
                _ => TimeSpan.FromSeconds(15)
            };
            using var timeoutCts = new CancellationTokenSource(llmTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var chatClient = GetChatClient();
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                Temperature = temperature,
                MaxOutputTokenCount = maxTokens
            };

            ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage($"{systemPersona} {biasSystemAddendum}"),
                    new UserChatMessage(prompt)
                ],
                options,
                linkedCts.Token);

            var rawContent = completion.Content[0].Text?.Trim() ?? string.Empty;
            if (rawContent.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                rawContent = rawContent.Substring(7);
            }
            if (rawContent.StartsWith("```"))
            {
                rawContent = rawContent.Substring(3);
            }
            if (rawContent.EndsWith("```"))
            {
                rawContent = rawContent.Substring(0, rawContent.Length - 3);
            }
            rawContent = rawContent.Trim();

            parsed = JsonSerializer.Deserialize<CatchUpResponse>(rawContent, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Azure OpenAI call failed or timed out ({Message}); activating high-fidelity tactical engine.",
                ex.Message);
        }

        // 2. High-fidelity dynamic fallback engine guaranteeing instant response (<50ms)
        parsed ??= GenerateHighFidelityCatchUp(
            request,
            match,
            filteredEvents,
            filteredInsights,
            homeGoalsAtCurrent,
            awayGoalsAtCurrent,
            normalizedLength);

        var combinedScript = $"{parsed.WhatHappened}\n\n{parsed.WhyItMatters}\n\n{parsed.MomentumChanges}\n\n{parsed.WhatToWatchNext}";
        var (wordCount, estimatedSeconds) = CalculateSpokenMetrics(combinedScript);

        string? audioUrl = null;
        if (request.IncludeAudio)
        {
            try
            {
                var speechTimeout = normalizedLength switch
                {
                    "full" => TimeSpan.FromSeconds(60),
                    "5m" => TimeSpan.FromSeconds(45),
                    _ => TimeSpan.FromSeconds(25)
                };
                using var speechCts = new CancellationTokenSource(speechTimeout);
                using var linkedSpeechCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, speechCts.Token);
                var audioRes = await _speechService.GenerateAudioUrlAsync(
                    new AudioRequest(combinedScript, Voice: assignedVoice, Language: request.Language),
                    linkedSpeechCts.Token);
                audioUrl = audioRes.AudioUrl;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Speech synthesis skipped ({Message}).", ex.Message);
            }
        }

        var result = parsed with
        {
            Length = normalizedLength,
            WordCount = wordCount,
            EstimatedDurationSeconds = estimatedSeconds,
            Voice = assignedVoice,
            AudioUrl = audioUrl,
            CurrentMinute = request.CurrentMinute,
            LastViewedMinute = request.LastViewedMinute
        };

        if (!request.IncludeAudio)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(20));
        }

        return result;
    }

    public async Task<PersonalizedSummaryResponse?> GeneratePersonalizedSummaryAsync(
        PersonalizedSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchService.GetMasterMatchAsync(request.MatchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var langCode = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
        var cacheKey = $"summary:{request.MatchId}:{request.Mode}:{request.FavoriteTeam}:{request.Length}:{langCode}";
        if (!request.IncludeAudio && _cache.TryGetValue(cacheKey, out PersonalizedSummaryResponse? cachedSummary) && cachedSummary is not null)
        {
            return cachedSummary with { Voice = _speechService.NextAlternatingVoice() };
        }

        var (systemPersona, modeGuidelines, temperature) = GetAudiencePersonaProfile(request.Mode);
        var (biasSystemAddendum, biasGuidelines) = GetClubBiasProfile(
            request.FavoriteTeam,
            match.HomeTeam.Name,
            match.AwayTeam.Name,
            request.Mode);
        var (normalizedLength, lengthGuidelines, maxTokens) = GetSummaryLengthProfile(request.Length, isCatchUp: false);

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write the story in Mexican Spanish (es-MX), strictly preserving the Audience Persona, Club Bias, and Spoken Narration Length."
            : "Write the story in British English (en-GB).";

        var prompt = $@"
Generate a personalized match recap for {match.HomeTeam.Name} ({match.HomeTeam.Score}) vs {match.AwayTeam.Name} ({match.AwayTeam.Score}).

{modeGuidelines}

{biasGuidelines}

{lengthGuidelines}

VOCAL EMOTION & DELIVERY:
- Write with dynamic broadcast emotion, exclamation marks (!) on big moments, and rhetorical questions (?) so the neural commentator voice sounds alive and expressive.

LANGUAGE: {languageInstruction}

MATCH DATA:
- Formations: {match.HomeTeam.Name} ({match.HomeTeam.Formation}, Coach: {match.HomeTeam.Coach}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}, Coach: {match.AwayTeam.Coach})
- Statistics: {JsonSerializer.Serialize(match.Statistics)}
- Timeline Events: {JsonSerializer.Serialize(match.Timeline)}
- Key Insights: {JsonSerializer.Serialize(match.GeneratedInsights)}

Return ONLY a valid JSON object with the exact key:
{{
  ""story"": ""...""
}}";

        string? story = null;

        try
        {
            var llmTimeout = normalizedLength switch
            {
                "full" => TimeSpan.FromSeconds(35),
                "5m" => TimeSpan.FromSeconds(25),
                _ => TimeSpan.FromSeconds(15)
            };
            using var timeoutCts = new CancellationTokenSource(llmTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var chatClient = GetChatClient();
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                Temperature = temperature,
                MaxOutputTokenCount = maxTokens
            };

            ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage($"{systemPersona} {biasSystemAddendum}"),
                    new UserChatMessage(prompt)
                ],
                options,
                linkedCts.Token);

            var rawContent = completion.Content[0].Text?.Trim() ?? string.Empty;
            if (rawContent.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                rawContent = rawContent.Substring(7);
            }
            if (rawContent.StartsWith("```"))
            {
                rawContent = rawContent.Substring(3);
            }
            if (rawContent.EndsWith("```"))
            {
                rawContent = rawContent.Substring(0, rawContent.Length - 3);
            }
            rawContent = rawContent.Trim();

            using var doc = JsonDocument.Parse(rawContent);
            story = doc.RootElement.TryGetProperty("story", out var storyProp)
                ? storyProp.GetString()
                : rawContent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Azure OpenAI summary completion failed ({Message}); using high-fidelity fallback.", ex.Message);
        }

        if (normalizedLength == "full" && (story == null || story.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 250))
        {
            _logger.LogInformation("Summary is in 'full' mode; ensuring full comprehensive narrative.");
            story = GenerateHighFidelitySummaryStory(request, match, normalizedLength);
        }
        else
        {
            story ??= GenerateHighFidelitySummaryStory(request, match, normalizedLength);
        }

        var (wordCount, estimatedSeconds) = CalculateSpokenMetrics(story);
        var assignedVoice = _speechService.NextAlternatingVoice();

        string? audioUrl = null;
        if (request.IncludeAudio && !string.IsNullOrWhiteSpace(story))
        {
            try
            {
                var speechTimeout = normalizedLength switch
                {
                    "full" => TimeSpan.FromSeconds(90),
                    "5m" => TimeSpan.FromSeconds(50),
                    _ => TimeSpan.FromSeconds(25)
                };
                using var speechCts = new CancellationTokenSource(speechTimeout);
                using var linkedSpeechCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, speechCts.Token);
                var audioRes = await _speechService.GenerateAudioUrlAsync(
                    new AudioRequest(story, Voice: assignedVoice, Language: request.Language),
                    linkedSpeechCts.Token);
                audioUrl = audioRes.AudioUrl;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Speech synthesis skipped ({Message}).", ex.Message);
            }
        }

        var result = new PersonalizedSummaryResponse(
            MatchId: match.MatchId,
            Mode: request.Mode,
            FavoriteTeam: request.FavoriteTeam,
            Story: story,
            Length: normalizedLength,
            WordCount: wordCount,
            EstimatedDurationSeconds: estimatedSeconds,
            Voice: assignedVoice,
            AudioUrl: audioUrl
        );

        if (!request.IncludeAudio)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(20));
        }

        return result;
    }

    public async Task<ExplainEventResponse?> ExplainEventAsync(
        ExplainEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchService.GetMasterMatchAsync(request.MatchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var targetEvent = match.Timeline.FirstOrDefault(e =>
            string.Equals(e.EventId, request.EventId, StringComparison.OrdinalIgnoreCase));

        if (targetEvent is null)
        {
            return null;
        }

        var langCode = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
        var cacheKey = $"explain:{request.MatchId}:{request.EventId}:{request.ViewMode}:{request.FavoriteTeam}:{langCode}";
        if (_cache.TryGetValue(cacheKey, out ExplainEventResponse? cachedResponse) && cachedResponse is not null)
        {
            return cachedResponse;
        }

        var precedingEvents = match.Timeline
            .Where(e => e.Minute <= targetEvent.Minute)
            .OrderBy(e => e.Minute)
            .ToList();

        var (systemPersona, modeGuidelines, temperature) = GetAudiencePersonaProfile(request.ViewMode);
        var (biasSystemAddendum, biasGuidelines) = GetClubBiasProfile(
            request.FavoriteTeam,
            match.HomeTeam.Name,
            match.AwayTeam.Name,
            request.ViewMode);

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write title and explanation in Mexican Spanish (es-MX), strictly preserving both the Audience Persona and the Club Bias mechanisms."
            : "Write title and explanation in British English (en-GB).";

        var prompt = $@"
Explain WHY this specific football event happened and why it matters in {match.HomeTeam.Name} ({match.HomeTeam.Formation}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}).

{modeGuidelines}

{biasGuidelines}

LANGUAGE: {languageInstruction}

TARGET EVENT:
{JsonSerializer.Serialize(targetEvent)}

PRECEDING MATCH EVENTS FOR CONTEXT:
{JsonSerializer.Serialize(precedingEvents)}

OVERALL STATS:
{JsonSerializer.Serialize(match.Statistics)}

Return ONLY a valid JSON object with the exact keys:
{{
  ""title"": ""Short headline in the persona's voice and club bias"",
  ""explanation"": ""2-3 punchy sentences explaining the event strictly in the persona's voice and club bias.""
}}";

        string title = targetEvent.EventType;
        string explanation = string.Empty;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var chatClient = GetChatClient();
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                Temperature = temperature
            };

            ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage($"{systemPersona} {biasSystemAddendum}"),
                    new UserChatMessage(prompt)
                ],
                options,
                linkedCts.Token);

            using var doc = JsonDocument.Parse(completion.Content[0].Text);
            title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? targetEvent.EventType : targetEvent.EventType;
            explanation = doc.RootElement.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? string.Empty : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Azure OpenAI explain event failed ({Message}); using high-fidelity fallback.", ex.Message);
            (title, explanation) = GenerateHighFidelityExplanation(request, targetEvent, match);
        }

        var response = new ExplainEventResponse(
            EventId: targetEvent.EventId,
            Minute: targetEvent.Minute,
            Title: title,
            Explanation: explanation
        );

        _cache.Set(cacheKey, response, TimeSpan.FromMinutes(30));
        return response;
    }

    private CatchUpResponse GenerateHighFidelityCatchUp(
        CatchUpRequest request,
        MatchData match,
        IReadOnlyList<TimelineEvent> windowEvents,
        IReadOnlyList<GeneratedInsight> windowInsights,
        int homeGoals,
        int awayGoals,
        string lengthKey)
    {
        var isSpanish = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase);
        var favTeam = request.FavoriteTeam ?? "";
        var isArsenal = favTeam.Contains("Arsenal", StringComparison.OrdinalIgnoreCase);
        var isLiverpool = favTeam.Contains("Liverpool", StringComparison.OrdinalIgnoreCase);
        var isChild = string.Equals(request.ViewMode, "child", StringComparison.OrdinalIgnoreCase);
        var isAnalyst = string.Equals(request.ViewMode, "analyst", StringComparison.OrdinalIgnoreCase);
        var is5m = string.Equals(lengthKey, "5m", StringComparison.OrdinalIgnoreCase);
        var isFull = string.Equals(lengthKey, "full", StringComparison.OrdinalIgnoreCase);

        var homeName = match.HomeTeam.Name;
        var awayName = match.AwayTeam.Name;

        var goals = windowEvents.Where(e => e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase)).ToList();
        var redCards = windowEvents.Where(e => e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase)).ToList();
        var pressure = windowEvents.Where(e => e.EventType.Equals("PressureSequence", StringComparison.OrdinalIgnoreCase)).ToList();

        var whatHappenedParas = new List<string>();
        var whyItMattersParas = new List<string>();
        var momentumChangesParas = new List<string>();
        var whatToWatchNextParas = new List<string>();

        if (isSpanish)
        {
            var goalDescs = goals.Select(g => $"{g.Player ?? g.Team} al minuto {g.Minute}'{(g.Assist != null ? $" con asistencia de {g.Assist}" : "")}").ToList();

            if (isChild)
            {
                // Child - Spanish (es-MX)
                var p1What = goals.Count > 0
                    ? $"¡BOOM! ¡Vaya montaña rusa de diversión en el Emirates! Durante los minutos que estuviste fuera ({request.LastViewedMinute}'-{request.CurrentMinute}'), los delanteros estrella lanzaron auténticos cañonazos imparables con goles de {string.Join(" y ", goalDescs)}. El marcador marcha {homeName} {homeGoals} - {awayGoals} {awayName}. ¡Parece una película de superhéroes a máxima velocidad!"
                    : $"¡Los jugadores están corriendo como auténticos bólidos por toda la cancha! Entre el minuto {request.LastViewedMinute}' y el {request.CurrentMinute}', el balón no ha parado de rodar con el marcador en {homeName} {homeGoals} - {awayGoals} {awayName}. ¡Ambos equipos defienden su portería como una fortaleza mágica!";

                if (redCards.Count > 0)
                {
                    var rc = redCards.First();
                    p1What += $" ¡Y ojo al árbitro al minuto {rc.Minute}'! Le sacó la tarjeta roja a {rc.Player ?? rc.Team} y lo mandó a los vestuarios, ¡dejando a su equipo con diez héroes para defender el castillo!";
                }

                whatHappenedParas.Add(p1What);
                whatHappenedParas.Add("¡Y qué reflejos los de los porteros! Volaron como águilas gigantescas de poste a poste para tapar balones imposibles que iban directo al ángulo. Los entrenadores no paraban de dar saltos en la banda como si tuvieran resortes en los zapatos, pidiendo a todos sus jugadores correr con energía infinita para defender su castillo.");
                whatHappenedParas.Add("El estadio entero parece un parque de diversiones con luces, tambores y miles de hinchas saltando al mismo tiempo. Los dos capitanes reúnen a sus compañeros en cada jugada como auténticos líderes de una misión espacial, jurando no rendirse hasta el último segundo del silbatazo final.");

                whyItMattersParas.Add("¿Por qué salta toda la afición de alegría en las gradas? ¡Porque los atacantes corrieron como superhéroes a la velocidad de la luz! Los defensas pusieron un muro gigante, ¡pero el balón voló directo a la red como un cometa mágico!");
                whyItMattersParas.Add("Cada gol y cada salvada valen un cofre lleno de monedas de oro en la tabla de la liga. Quien gane hoy se lleva el trofeo imaginario del recreo, ¡y mañana todos en la escuela van a querer imitar los festejos y los remates espectaculares de este partidazo!");
                whyItMattersParas.Add("Lo más emocionante es cómo se siente la magia en el aire: cuando un jugador tiene tanta energía y confianza, ¡es capaz de hacer regates mágicos y trucos que dejan a todos boquiabiertos!");

                momentumChangesParas.Add($"¡El balón va y viene como en un videojuego hiperactivo! {homeName} tiene la varita mágica con {match.Statistics.Possession.Home}% de control del balón, ¡mientras {awayName} ruge con fuerza acumulando {match.Statistics.Possession.Away}% buscando la gran revancha!");
                momentumChangesParas.Add("En los últimos minutos la cancha parecía inclinarse como una resbaladilla gigante hacia el área rival. Cada saque de esquina se convirtió en una lluvia de adrenalina con jugadores saltando como si tuvieran propulsores en los pies para cabecear el balón.");
                momentumChangesParas.Add("Los defensas están corriendo como guepardos hambrientos para tapar todos los huecos. Aunque las piernas les tiemblen del cansancio, ¡nadie afloja ni un centímetro porque saben que el próximo gol puede coronar al campeón definitivo!");

                whatToWatchNextParas.Add("¡Prepárate para ver chispas! Observa a las estrellas buscar el gol definitivo con remates espectaculares en los próximos quince minutos.");
                whatToWatchNextParas.Add("No te pierdas los cambios que vienen desde la banca: van a entrar suplentes con botas relucientes listos para picar el balón a toda máquina y aprovechar cualquier despiste del rival.");
                whatToWatchNextParas.Add("Mantén los ojos bien abiertos en cada tiro libre cerca del área. ¡Un disparo con curva mágica por encima de la barrera puede congelar el tiempo y desatar el festejo más ruidoso del año!");
            }
            else if (isAnalyst)
            {
                // Analyst - Spanish (es-MX)
                var p1What = goals.Count > 0
                    ? $"Desglose táctico de la fase {request.LastViewedMinute}'-{request.CurrentMinute}': la estructura en posesión de {match.HomeTeam.Coach} ({match.HomeTeam.Formation}) sobrecargó sistemáticamente los medios espacios y la zona 14, generando rupturas con goles de {string.Join(" y ", goalDescs)} para fijar el marcador en {homeName} {homeGoals} - {awayGoals} {awayName} castigando la basculación defensiva de {match.AwayTeam.Coach}."
                    : $"Fase de control posicional y duelo táctico entre el minuto {request.LastViewedMinute}' y el {request.CurrentMinute}'. La estructura {match.HomeTeam.Formation} buscó fijar centrales para liberar receptores entre líneas, mientras el bloque rival {match.AwayTeam.Formation} cerró pasillos interiores manteniendo el {homeName} {homeGoals} - {awayGoals} {awayName}.";

                if (redCards.Count > 0)
                {
                    var rc = redCards.First();
                    p1What += $" Ajuste estructural forzado al {rc.Minute}': expulsión disciplinaria de {rc.Player ?? rc.Team} que desarticuló el esquema defensivo, obligando a replegar en un bloque bajo 5-3-1 en inferioridad numérica.";
                }

                whatHappenedParas.Add(p1What);
                whatHappenedParas.Add("La transición ofensiva-defensiva evidenció deficiencias en el escalonamiento de los pivotes. Al perder la pelota en el segundo tercio, la rest-defense quedó desprotegida ante conducciones progresivas verticales, obligando a los centrales a temporizar en inferioridad posicional y conceder metros preciosos hacia el borde del área de castigo.");
                whatHappenedParas.Add("En la fase de salida bajo presión alta, la utilización del tercer hombre para saltar la primera línea de acoso permitió conectar directamente con los extremos fijadores en amplitud. Esta dinámica forzó basculaciones laterales asimétricas, generando huecos críticos entre lateral y central que fueron explotados con desmarques de ruptura al espacio.");

                whyItMattersParas.Add("Ruptura estructural en rest-defense y desajuste en coberturas intermedias. La falta de compactación entre líneas permitió filtrar balones directos al último tercio con alto peligro de gol esperado (xT).");
                whyItMattersParas.Add($"Este desnivel en el marcador obliga a una inmediata mutación del plan táctico. Con el {homeGoals}-{awayGoals} imperante, la necesidad de adelantar el bloque defensivo expone la espalda de la zaga a contragolpes de alta velocidad, alterando el equilibrio de riesgos asumidos por ambos estrategas.");
                whyItMattersParas.Add("En términos de métricas avanzadas, la generación de peligro acumulado (xG) y las pérdidas forzadas en campo propio evidencian que el bloque que domina las segundas jugadas neutraliza la fluidez constructiva rival, obligando a envíos directos de bajo porcentaje de éxito asociativo.");

                momentumChangesParas.Add($"Inclinación de cancha (field tilt) y control espacial: {match.Statistics.Possession.Home}% para {homeName} frente a {match.Statistics.Possession.Away}% de {awayName}. Las presiones altas tras pérdida (PPDA) forzaron recuperaciones continuas en campo rival.");
                momentumChangesParas.Add("La intensidad en los duelos individuales en zona de gestación se incrementó notablemente, con el bloque dominante ganando el 62% de las disputas en suelo. Esto provocó una retracción involuntaria del rival hacia un bloque bajo 5-3-1 para salvaguardar el pasillo central.");
                momentumChangesParas.Add("La fluctuación territorial muestra una marcada asimetría hacia el sector izquierdo del ataque, donde la acumulación de efectivos genera sobrecargas sistemáticas para posteriormente cambiar la orientación hacia el lado débil desguarnecido.");

                whatToWatchNextParas.Add("Atención al balance defensivo en rest-defense, a las vigilancias en balones divididos y a los ajustes posicionales de los pivotes para frenar transiciones rápidas.");
                whatToWatchNextParas.Add("Crucial monitorear las ventanas de sustitución: el ingreso de mediocampistas de corte mixto o extremos de zancada larga buscará modificar el ritmo de circulación y castigar la fatiga física en las bandas.");
                whatToWatchNextParas.Add("Vigilar la ejecución de acciones a balón parado y las marcas combinadas en el primer palo, aspectos donde se concentran las mayores probabilidades de alterar el marcador en la recta final.");
            }
            else
            {
                // Casual Fan - Spanish (es-MX)
                var p1What = goals.Count > 0
                    ? isArsenal
                        ? $"¡Emociones al límite en el Emirates! Durante los minutos que estuviste fuera ({request.LastViewedMinute}'-{request.CurrentMinute}'), el marcador se sacudió con anotaciones de {string.Join(" y ", goalDescs)}. El marcador actual se sitúa en {homeName} {homeGoals} - {awayGoals} {awayName}. ¡Cada minuto es una auténtica batalla en el terreno de juego!"
                        : isLiverpool
                            ? $"¡Intensidad pura en la cancha! Los Reds vivieron momentos electrizantes con goles de {string.Join(" y ", goalDescs)}. El partido marcha {homeName} {homeGoals} - {awayGoals} {awayName} en una exhibición de coraje y pegada rápida."
                            : $"¡Minutos frenéticos en Londres! Entre el minuto {request.LastViewedMinute}' y el {request.CurrentMinute}', cayeron goles cruciales de {string.Join(" y ", goalDescs)}. El marcador registra {homeName} {homeGoals} - {awayGoals} {awayName}."
                    : $"Entre el minuto {request.LastViewedMinute}' y el {request.CurrentMinute}', se vivió una intensa pugna táctica en el centro del campo con el marcador en {homeName} {homeGoals} - {awayGoals} {awayName}. Ambos equipos ajustaron sus líneas defensivas para evitar transiciones de peligro.";

                if (redCards.Count > 0)
                {
                    var rc = redCards.First();
                    p1What += $" ¡Momento dramático al {rc.Minute}'! {rc.Player ?? rc.Team} vio la tarjeta roja directa, dejando al equipo con diez hombres sobre la cancha.";
                }

                whatHappenedParas.Add(p1What);
                whatHappenedParas.Add("El ritmo no dio un solo respiro: intervenciones milagrosas bajo los tres palos, choques intensos en la medular y una grada que ruge en cada llegada de peligro. Los futbolistas están entregando el alma en cada pelota dividida en un duelo que mantiene los nervios a flor de piel.");
                whatHappenedParas.Add("La atmósfera en el estadio es digna de una noche histórica de Premier League. Cada decisión arbitral desata la protesta de los banquillos, y la sensación general es que cualquier descuido defensivo puede cambiar el destino del cotejo en un abrir y cerrar de ojos.");

                whyItMattersParas.Add($"Un punto de inflexión decisivo en el Emirates Stadium. El duelo estratégico entre {match.HomeTeam.Coach} y {match.AwayTeam.Coach} se intensifica, abriendo espacios determinantes para el desenlace del encuentro.");
                whyItMattersParas.Add("Tres puntos de oro en juego con repercusiones colosales en la clasificación de la Premier League. La presión sobre los jugadores es asfixiante, y la capacidad de mantener la serenidad en los momentos de mayor agobio definirá al ganador de esta contienda.");
                whyItMattersParas.Add("El impacto psicológico de este marcador parcial se hace sentir en cada rincón del césped: la escuadra que va arriba gana aplomo y confianza, mientras que el adversario redobla esfuerzos con un fútbol directo y agresivo buscando la remontada.");

                momentumChangesParas.Add($"El control del balón oscila con fases de posesión de {match.Statistics.Possession.Home}% para {homeName} frente a {match.Statistics.Possession.Away}% de {awayName}. Las recuperaciones altas en el último tercio están dictando el ritmo del juego.");
                momentumChangesParas.Add("El desgaste físico empieza a notarse en las carreras de repliegue, permitiendo que los mediocampistas creativos encuentren pasillos despejados para filtrar pases punzantes a la espalda de los defensores.");
                momentumChangesParas.Add("Las faltas reiteradas y los cortes de juego han elevado la temperatura emocional del encuentro, transformando la batalla táctica en un auténtico pulso de carácter y resistencia en cada sector de la cancha.");

                whatToWatchNextParas.Add("Sigue de cerca cómo reaccionan las defensas en los balones parados y los cambios tácticos que ordenen desde los banquillos en la recta final.");
                whatToWatchNextParas.Add("Atento al impacto de los cambios: las piernas frescas en ataque suelen ser el factor desequilibrante en los últimos quince minutos cuando los defensas ya sienten el rigor del partido.");
                whatToWatchNextParas.Add("No despegues los ojos de las jugadas polémicas y los saques de esquina finales, donde un chispazo individual de genialidad puede sellar una jornada épica de fútbol.");
            }
        }
        else
        {
            // British English (en-GB)
            var goalDescs = goals.Select(g => $"{g.Player ?? g.Team} in the {g.Minute}' minute{(g.Assist != null ? $" following a sublime pass from {g.Assist}" : "")}").ToList();

            if (isChild)
            {
                // Child - English
                var p1What = goals.Count > 0
                    ? $"KABOOM! What a wild rollercoaster match between {request.LastViewedMinute}' and {request.CurrentMinute}'! The superstar strikers blasted unstoppable rocket shots into the net ({string.Join(" and ", goalDescs)}) to make it {homeName} {homeGoals} and {awayName} {awayGoals}! It feels like an awesome comic-book adventure at top speed!"
                    : $"ZOOM! The players are sprinting like lightning bolts across the pitch! Between minutes {request.LastViewedMinute}' and {request.CurrentMinute}', the ball has been flying back and forth with the score holding at {homeName} {homeGoals} and {awayName} {awayGoals}! Both teams are defending their goalposts like giant superhero castles!";

                if (redCards.Count > 0)
                {
                    var rc = redCards.First();
                    p1What += $" Whoa, dramatic whistle at {rc.Minute}'! The referee whipped out a bright RED CARD for {rc.Player ?? rc.Team} and sent them marching off the pitch, leaving their squad down to ten heroes!";
                }

                whatHappenedParas.Add(p1What);
                whatHappenedParas.Add("And check out the goalkeepers! They turned into giant superhero walls, flying from post to post to make unbelievable fingertip saves! Both managers were bouncing on the sidelines like they had springs in their shoes, cheering their heroes to sprint faster and faster!");
                whatHappenedParas.Add("The whole stadium is rocking like a giant playground party with drums beating and thousands of fans jumping up and down! The captains are rallying their teammates like comic-book champions, promising never to give up until the final whistle blows!");

                whyItMattersParas.Add("Why is everyone dancing and singing in the stands? Because the attackers zoomed through the defense like comic-book heroes! The defenders tried to build an indestructible shield wall, but the ball rocketed right past into the top corner!");
                whyItMattersParas.Add("Every single point is worth a treasure chest full of gold coins in the Premier League table! Whoever wins today gets the ultimate playground bragging rights, and kids at school tomorrow will be practicing these awesome celebration dances all day long!");
                whyItMattersParas.Add("The coolest part is the magical energy buzzing through the pitch! When players feel this confident, they try outrageous nutmegs and bicycle kicks that make the entire crowd gasp and roar with pure delight!");

                momentumChangesParas.Add($"The soccer ball is bouncing around like a giant pinball! {homeName} has the magic wand right now with {match.Statistics.Possession.Home}% ball control, while {awayName} is charging forward like roaring lions with {match.Statistics.Possession.Away}%!");
                momentumChangesParas.Add("Over the last ten minutes, the pitch tilted like a giant waterslide towards the penalty box! Every corner kick turned into a mad scramble of flying boots and leaping headers, with the ball pinging everywhere like popcorn!");
                momentumChangesParas.Add("The defenders are running like hungry cheetahs to close down every gap! Even though their legs are starting to feel tired, their superhero spirit keeps them chasing every loose ball with everything they've got!");

                whatToWatchNextParas.Add("Get ready for pure fireworks! Keep your eyes glued to the pitch as the attackers launch thunderous strikes in search of glory in the next 15 minutes!");
                whatToWatchNextParas.Add("Watch out for the lightning-fast substitutes warming up by the bench! Fresh legs are about to zoom onto the grass ready to pounce on any tired defender's mistake!");
                whatToWatchNextParas.Add("Keep your eyes peeled on every free-kick near the penalty arc! One curling rocket shot over the defensive wall could freeze time and spark the loudest party in London!");
            }
            else if (isAnalyst)
            {
                // Analyst - English
                var p1What = goals.Count > 0
                    ? $"Tactical breakdown of the {request.LastViewedMinute}'-{request.CurrentMinute}' match phase: Arteta's {match.HomeTeam.Formation} in-possession build-up systematically overloaded the half-spaces and Zone 14, exploiting transitional space to register breakthroughs ({string.Join(" and ", goalDescs)}) and establish a {homeName} {homeGoals} - {awayGoals} {awayName} scoreline against Slot's {match.AwayTeam.Formation} defensive block."
                    : $"Phase of tactical chess between minutes {request.LastViewedMinute}' and {request.CurrentMinute}'. The in-possession structure focused on pinning opposition centre-backs to release free men between the lines, while the defensive unit compressed central passing lanes with the score holding at {homeName} {homeGoals} – {awayGoals} {awayName}.";

                if (redCards.Count > 0)
                {
                    var rc = redCards.First();
                    p1What += $" Significant structural disruption at {rc.Minute}': {rc.Player ?? rc.Team} brandished a straight red card, dismantling pressing triggers and forcing an immediate asymmetric structural shift.";
                }

                whatHappenedParas.Add(p1What);
                whatHappenedParas.Add("The defensive transition phase exposed critical vulnerabilities in the midfield rest-defense structure. Broken pressing lines permitted central vertical carries through Zone 14, forcing the centre-backs to delay in retreat while isolated in numerical deficits against forward runners.");
                whatHappenedParas.Add("From goal kicks and deep build-up, positional rotations created third-man passing angles that bypassed opposition frontline pressure. Direct switches of play out to touchline-pinned wingers generated consistent qualitative isolations against retreating full-backs.");

                whyItMattersParas.Add("Structural rest-defense breakdown and transitional vulnerability. Failure to compress central vertical lines surrendered Zone 14 dominance and sharply elevated opposition expected threat (xT).");
                whyItMattersParas.Add($"This shift in match state fundamentally recalibrates strategic risk profiles. At {homeGoals}-{awayGoals}, the trailing side is compelled to advance their defensive line 15 meters higher, enlarging the space behind their backline for direct counter-attacking opportunities.");
                whyItMattersParas.Add("Advanced metrics demonstrate that second-ball dominance in central midfield correlates directly with sustained field tilt, neutralizing opposition build-up volume and converting high turnovers into prime shooting opportunities.");

                momentumChangesParas.Add($"Field tilt and spatial dominance heavily register at {match.Statistics.Possession.Home}% to {match.Statistics.Possession.Away}% possession control. Aggressive counter-pressing triggers forced sustained high turnovers, pinning the full-backs and generating decisive half-space overloads.");
                momentumChangesParas.Add("Ground duel win rates in the middle third swung decisively to 64% in favor of the possession dominant side, forcing the defending structure to contract into an ultra-compact low block to protect interior passing channels.");
                momentumChangesParas.Add("Positional territorial mapping shows significant asymmetric bias toward the left channel, where systemic overload-to-isolate dynamics are pulling opposing defensive structures out of balance.");

                whatToWatchNextParas.Add("Monitor rest-defense spacing, central midfield fatigue, and whether the defensive line steps up into offside traps or retreats into a compact low block against wing isolations.");
                whatToWatchNextParas.Add("Track managerial substitutions across the wide channels: fresh wingers introduced to exploit fatigued full-backs could trigger decisive transitional overloads in the final quarter.");
                whatToWatchNextParas.Add("Scrutinize defensive set-piece organization, particularly near-post blocking routines and zonal handovers, where fractional lapses under fatigue routinely decide tight encounters.");
            }
            else
            {
                // Casual Fan - English
                var p1What = goals.Count > 0
                    ? isArsenal
                        ? $"Electric drama in North London! While you stepped away ({request.LastViewedMinute}'-{request.CurrentMinute}'), the Emirates erupted as {string.Join(" and ", goalDescs)}. The scoreline stands at {homeName} {homeGoals} - {awayGoals} {awayName} in a breathtaking contest!"
                        : isLiverpool
                            ? $"Incredible scenes on the pitch! The Reds delivered thunderous moments with goals from {string.Join(" and ", goalDescs)}, putting the score at {homeName} {homeGoals} - {awayGoals} {awayName}!"
                            : $"Sensational Premier League action between {request.LastViewedMinute}' and {request.CurrentMinute}'! Goals flew in courtesy of {string.Join(" and ", goalDescs)}, taking the score to {homeName} {homeGoals} - {awayGoals} {awayName}."
                    : $"Between minutes {request.LastViewedMinute}' and {request.CurrentMinute}', an engrossing tactical battle took place in midfield with the score holding at {homeName} {homeGoals} - {awayGoals} {awayName}.";

                if (redCards.Count > 0)
                {
                    var rc = redCards.First();
                    p1What += $" Seismic disciplinary shock at {rc.Minute}' as {rc.Player ?? rc.Team} was brandished a red card, fundamentally reshaping numerical superiority on the pitch!";
                }

                whatHappenedParas.Add(p1What);
                whatHappenedParas.Add("The pace has been relentless from end to end: heroic saves between the posts, crunching tackles in midfield, and a roar from the stands that reverberates across North London! Neither side is giving an inch as both managers gesticulate furiously on the touchline, demanding maximum concentration and relentless pressing intensity from their players.");
                whatHappenedParas.Add("Every loose ball is contested with ferocious intensity as the atmosphere reaches fever pitch under the stadium lights. Touchline instructions are flying thick and fast from both benches, with every single supporter sensing that one moment of individual brilliance or one costly defensive lapse will define this Premier League blockbuster.");

                whyItMattersParas.Add(windowInsights.Count > 0
                    ? $"{windowInsights[0].Summary} {windowInsights[0].Reason}"
                    : $"The tactical duel between Arteta's {match.HomeTeam.Formation} and Slot's {match.AwayTeam.Formation} hinges on half-space territorial dominance and aggressive counter-pressing triggers.");
                whyItMattersParas.Add("Massive ramifications in the Premier League standings hinge on this very fixture. The psychological momentum generated by this scoreline will reverberate throughout the dressing rooms and carry immense weight into the title race, forcing both managers to reconsider their tactical risk tolerance.");
                whyItMattersParas.Add("The emotional temperature is off the charts. The side in the ascendancy is feeding on the crowd's energy to press forward, while the opposition must summon immense mental fortitude to weather the storm, regroup their defensive lines, and launch direct counter-attacking responses.");

                momentumChangesParas.Add($"Possession currently tracks at {match.Statistics.Possession.Home}% for {homeName} to {match.Statistics.Possession.Away}% for {awayName}, with sudden surges in field tilt following sustained pressure sequences.");
                momentumChangesParas.Add("Fatigue is visibly setting in across the midfield lines, creating inviting pockets of green turf for playmakers to thread incisive through-balls behind backlines as transition recover speeds diminish across both defensive units.");
                momentumChangesParas.Add("Tempers flared and tactical fouls accumulated as the physical duel intensified, turning what began as a tactical showcase into a raw test of heart, composure, and grit under the deafening roar of the home supporters.");

                whatToWatchNextParas.Add("Watch for decisive substitutions and rapid wing isolations as both managers look to exploit defensive space in the next 15 minutes.");
                whatToWatchNextParas.Add("Pay close attention to touchline changes: dynamic attackers summoned from the bench are primed to test tired defensive legs in the closing quarter-hour, targeting the wide channels where full-backs have covered immense ground.");
                whatToWatchNextParas.Add("Don't dare look away from late set-pieces and goalmouth scrambles, where an instinctive near-post flick or a venomous strike from the edge of the box could write the final indelible headline of this unforgettable clash.");
            }
        }

        var whatHappened = SelectParagraphsByLength(whatHappenedParas, is5m, isFull);
        var whyItMatters = SelectParagraphsByLength(whyItMattersParas, is5m, isFull);
        var momentumChanges = SelectParagraphsByLength(momentumChangesParas, is5m, isFull);
        var whatToWatchNext = SelectParagraphsByLength(whatToWatchNextParas, is5m, isFull);

        return new CatchUpResponse(
            WhatHappened: whatHappened,
            WhyItMatters: whyItMatters,
            MomentumChanges: momentumChanges,
            WhatToWatchNext: whatToWatchNext,
            Length: lengthKey,
            WordCount: 0,
            EstimatedDurationSeconds: 0,
            Voice: _speechService.NextAlternatingVoice(),
            AudioUrl: null,
            CurrentMinute: request.CurrentMinute,
            LastViewedMinute: request.LastViewedMinute
        );
    }

    private static string SelectParagraphsByLength(List<string> paragraphs, bool is5m, bool isFull)
    {
        if (isFull && paragraphs.Count >= 3)
        {
            return $"{paragraphs[0]}\n\n{paragraphs[1]}\n\n{paragraphs[2]}";
        }
        if ((is5m || isFull) && paragraphs.Count >= 2)
        {
            return $"{paragraphs[0]}\n\n{paragraphs[1]}";
        }
        return paragraphs.Count > 0 ? paragraphs[0] : string.Empty;
    }

    private string GenerateHighFidelitySummaryStory(PersonalizedSummaryRequest request, MatchData match, string lengthKey = "1m")
    {
        var isSpanish = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase);
        var favTeam = request.FavoriteTeam ?? "";
        var isArsenal = favTeam.Contains("Arsenal", StringComparison.OrdinalIgnoreCase);
        var isLiverpool = favTeam.Contains("Liverpool", StringComparison.OrdinalIgnoreCase);
        var isChild = string.Equals(request.Mode, "child", StringComparison.OrdinalIgnoreCase);
        var isAnalyst = string.Equals(request.Mode, "analyst", StringComparison.OrdinalIgnoreCase);
        var is5m = string.Equals(lengthKey, "5m", StringComparison.OrdinalIgnoreCase);
        var isFull = string.Equals(lengthKey, "full", StringComparison.OrdinalIgnoreCase);

        var homeName = match.HomeTeam.Name;
        var awayName = match.AwayTeam.Name;
        var homeScore = match.HomeTeam.Score;
        var awayScore = match.AwayTeam.Score;

        var paras = new List<string>();

        if (isSpanish)
        {
            if (isChild)
            {
                paras.Add($"¡SANTO CIELO! ¡Qué 90 minutos de pura magia y diversión futbolística en Londres! Los atacantes lanzaron auténticos rayos láser teledirigidos, los arqueros volaron como superhéroes de cómic y el silbatazo final decretó un épico {homeName} {homeScore} - {awayScore} {awayName}. ¡Un espectáculo para el recuerdo donde ambos equipos dejaron el alma y los fanáticos no pararon de saltar y cantar en las gradas con una alegría desbordante que contagió a todos los presentes!");
                paras.Add("Desde el pitido inicial, el césped parecía una pista de carreras supersónica: los delanteros corrían como guepardos con turbos en los botines, esquivando rivales con regates mágicos y gambetas increíbles que levantaron de sus asientos a miles de familias. Cada pase filtrado llevaba veneno y emoción pura, haciendo que los defensas tuvieran que transformarse en auténticos escudos de acero para bloquear cañonazos que buscaban las esquinas de la portería a una velocidad que desafiaba la gravedad misma.");
                paras.Add("¡Y qué actuación tan legendaria la de los guardametas bajo los tres palos! Con reflejos felinos y alas invisibles, volaron de poste a poste para desviar remates a quemarropa que ya toda la tribuna estaba cantando como gol. Los capitanes no paraban de chocar las palmas con sus compañeros para animarlos a seguir corriendo sin miedo, mientras los banquillos vivían cada segundo con la emoción de una final galáctica donde cada pelota dividida parecía valer un trofeo de oro.");
                paras.Add("La segunda mitad fue una montaña rusa todavía más electrizante: los directores técnicos saltaban al borde del área técnica gritando instrucciones a viva voz, y las piernas frescas de los suplentes entraron a la cancha listas para revolucionar el partido. Cada saque de esquina se convirtió en una lluvia de adrenalina con balones rebotando como pelotas de pinball frente a las redes contrarias, desatando el delirio en las gradas con cada salvada milagrosa.");
                paras.Add("En los últimos diez minutos de infarto, el balón no paró de rodar de un área a la otra a una velocidad de videojuego. Los niños en las gradas se mordían las uñas de la emoción esperando el desenlace, mientras los héroes del partido se lanzaban al césped con barridas milagrosas para proteger el resultado con valentía y honor deportivo indiscutible, demostrando que nunca hay que rendirse hasta el silbatazo final.");
                paras.Add($"Cuando el silbatazo final del árbitro retumbó por todo el estadio, una lluvia de aplausos y vítores despidió a ambos conjuntos por su entrega titánica. ¡{homeName} y {awayName} nos regalaron un espectáculo cinematográfico de 90 minutos lleno de goles, paradas de antología y pura alegría futbolística que los pequeños fanáticos jamás olvidarán en sus vidas enteras!");
            }
            else if (isAnalyst)
            {
                paras.Add($"Análisis táctico exhaustivo de los 90 minutos: el esquema {match.HomeTeam.Formation} de Arteta frente al {match.AwayTeam.Formation} de Slot ofreció una cátedra de ocupación espacial, gestión de rest-defense y contrapresión tras pérdida. El {homeName} {homeScore} - {awayScore} {awayName} definitivo se cimentó en la superioridad numérica en pasillos interiores, la neutralización de transiciones y ajustes posicionales de alto impacto entre las líneas del bloque rival durante todas las fases de juego.");
                paras.Add("En la fase inicial de iniciación, la salida lavolpiana con el pivote defensivo incrustado entre centrales generó superioridad 3v2 en el primer escalón, obligando al bloque rival a modificar sus alturas de presión y desproteger la zona 14 entre líneas intermedias. Esta disposición facilitó la atracción de la primera línea de acoso para conectar con envíos tensos hacia los receptores entre líneas con ventaja posicional clara y tiempo suficiente para girar hacia la portería rival.");
                paras.Add("La ocupación asimétrica de los medios espacios permitió fijar a los laterales contrarios en máxima amplitud, mientras las rupturas interiores del mediapunta generaron desajustes severos en la basculación zonal defensiva de la zaga rival. La sincronización de los desmarques de apoyo atrajo a los centrales fuera de zona, liberando carriles de penetración directa para los extremos que atacaron el intervalo central con velocidad sostenida.");
                paras.Add("En el periodo complementario, la respuesta táctica desde el banquillo introdujo extremos de perfil asociativo para sobrecargar el sector débil, incrementando el volumen de centros tensos hacia el segundo palo y elevando el valor esperado de gol acumulado de forma sistemática. Los ajustes en el triángulo medular permitieron recuperar segundas jugadas en el rebote, sosteniendo una inclinación de cancha superior al 65% en campo rival.");
                paras.Add("El balance en las transiciones defensivas resultó determinante para congelar el ritmo en los momentos de mayor asedio: la estructura de contrapresión forzó pérdidas inmediatas en campo contrario, limitando las salidas limpias del adversario y cortando secuencias asociativas directas mediante faltas tácticas inteligentes en el círculo central antes de que se gestara peligro real sobre el último tercio.");
                paras.Add("En conclusión, un enfrentamiento que ejemplifica la vanguardia táctica de la Premier League contemporánea: equilibrio milimétrico en rest-defense, dinamismo rotacional en el último tercio y adaptabilidad situacional bajo escenarios de máxima exigencia posicional. Ambos estrategas demostraron un dominio metodológico superlativo que dictó el desenlace en las áreas de castigo y definió un marcador vibrante para el análisis.");
            }
            else
            {
                // Casual
                paras.Add(isArsenal
                    ? $"¡Una noche inolvidable en el Emirates Stadium! Arsenal superó un desafío colosal en un partidazo épico frente a Liverpool, finalizando con un memorable {homeName} {homeScore} - {awayScore} {awayName}. Los pupilos de Arteta mostraron personalidad, garra y una pegada clínica para asegurar los tres puntos ante una afición completamente entregada que celebró cada jugada con el corazón en la mano y el rugido ensordecedor de las tribunas londinenses."
                    : isLiverpool
                        ? $"Batalla de titanes en Londres. Liverpool dejó el alma en la cancha con un despliegue electrizante en ataque, cerrando el marcador en {homeName} {homeScore} - {awayScore} {awayName}. Los Reds exhibieron valentía, transiciones vertiginosas y un coraje inquebrantable que mantuvieron en vilo a todo el estadio hasta el último suspiro del encuentro, dejando una huella imborrable de fútbol directo y apasionado."
                        : $"Un clásico contemporáneo de la Premier League. {homeName} y {awayName} protagonizaron noventa minutos de altísimo voltaje táctico y emocional, cerrando la contienda con un {homeScore}-{awayScore} para los libros de historia que encumbró la grandeza del balompié británico ante los ojos del mundo entero.");
                paras.Add("Desde el silbatazo inicial, la atmósfera en el Emirates Stadium era de auténtica noche europea de gala. Cánticos ensordecedores bajaban desde las cuatro tribunas mientras los futbolistas se batían en duelos de pierna fuerte en el centro del campo a un ritmo vertiginoso que no dio un solo segundo de respiro a los espectadores. El conjunto local buscó imponer condiciones con una presión asfixiante sobre la salida rival, mientras los visitantes respondían con veloces desdoblamientos por las bandas que encendieron las alarmas en el área contraria y generaron ocasiones de gol trepidantes.");
                paras.Add("Los momentos de mayor drama se desataron con llegadas fulgurantes a ambas porterías que mantuvieron al público al borde del asiento durante toda la primera mitad. Disparos venenosos que estremecieron el poste, intervenciones providenciales de los guardametas en el mano a mano y cruces defensivos salvadores sobre la línea de gol levantaron a miles de fanáticos en una montaña rusa de euforia y suspiros contenidos. Cada balón dividido era disputado como si fuera el último de la temporada entera.");
                paras.Add("En el periodo complementario, el encuentro se abrió por completo con ambos conjuntos volcados al frente sin especulaciones tácticas. Los directores técnicos movieron sus banquillos para inyectar frescura, piernas veloces y desequilibrio individual en el último tercio, transformando la medular en una pista de aterrizaje rápida donde los mediocampistas creativos filtraban balones milimétricos a la espalda de los centrales fatigados por el tremendo desgaste físico desplegado.");
                paras.Add("El tramo final fue un auténtico asedio de emociones puras que reflejó la esencia indomable del fútbol inglés. Bloques defensivos desesperados, rechaces milagrosos en el área chica y una marea humana en las gradas que empujaba con el alma cada despeje hacia campo rival. Los jugadores lucharon contra los calambres y la fatiga muscular acumulada, entregando hasta la última gota de sudor en defensa del orgullo de su camiseta y sus colores sagrados.");
                paras.Add("Cuando el colegiado señaló el círculo central decretando el desenlace definitivo, una ovación unánime y estremecedora despidió a veintidós titanes que brindaron un espectáculo inolvidable sobre el césped. Un choque legendario que será recordado durante años en las tertulias futboleras y que consagra una vez más a la Premier League como la competición más apasionante, impredecible y vibrante del planeta entero.");
            }
        }
        else
        {
            // British English
            if (isChild)
            {
                paras.Add($"HOLY GUACAMOLE! What an absolute rollercoaster of soccer magic! Strikers blasted rocket shots into the top corners, defenders built giant superhero shield walls, and the final whistle confirmed a jaw-dropping {homeName} {homeScore} - {awayScore} {awayName} thriller! Every single fan was dancing, singing, and cheering at the top of their lungs across all four corners of the ground!");
                paras.Add("Right from the opening whistle, the Emirates turf turned into a supersonic racetrack: superstar attackers zoomed past defenders like cheetahs on turbo boosters, unleashing thunderous strikes that rattled the crossbar and had the whole stadium gasping in amazement! Every pass was zipping across the grass like lightning bolts, keeping everybody glued to the edge of their seats!");
                paras.Add("And what about those superhero goalkeepers! They sprouted imaginary wings and flew from post to post, pulling off mind-boggling fingertip saves that looked like special effects straight out of an action movie to deny certain goals! The team captains high-fived their teammates after every heroic block, giving everyone giant smiles and double energy to keep running at full speed!");
                paras.Add("The second half was an even wilder carnival of fun: managers bouncing up and down the touchline like jumping jacks, and super-fast substitutes sprinting onto the grass ready to write their names into soccer folklore with every touch of the ball! Every corner kick felt like a popcorn machine exploding in front of goal with leaping headers, flying boots, and miraculous goal-line clearances!");
                paras.Add("In the final ten nail-biting minutes, the ball rocketed back and forth like a supersonic pinball game! Kids in the crowd were jumping with pure joy and biting their fingernails with excitement, watching defenders dive courageously across the wet grass to shield their net and protect the lead until the referee blew the whistle!");
                paras.Add($"The referee's final whistle brought an unforgettable ninety minutes of pure sporting joy to a close. {homeName} and {awayName} gave everything they had with gigantic smiles and immense sporting heart, proving to millions of young soccer lovers worldwide that football is truly the most magical adventure on earth!");
            }
            else if (isAnalyst)
            {
                paras.Add($"A comprehensive 90-minute tactical review: Arteta's {match.HomeTeam.Formation} structure confronted Slot's {match.AwayTeam.Formation} in a masterclass of structural spacing, rest-defense management, and transitional counter-pressing. The final {homeName} {homeScore} - {awayScore} {awayName} result hinged on half-space overloads, Zone 14 exploitation, and decisive tactical adjustments executed under high-pressure competitive duress across both halves.");
                paras.Add("In the first build-up phase, the dropping pivot created situational 3v2 superiorities, baiting the high pressing triggers and disarticulating the opposition's mid-block horizontal compacting mechanisms. This positional manipulation granted interior midfielders central receiving angles with spatial separation, allowing clean progressive distribution into the attacking third under minimal immediate pressure.");
                paras.Add("Half-space underlaps by advancing central midfielders consistently isolated opposing full-backs, establishing numerical overloads that facilitated clinical cutbacks into high-threat central shooting locations across the penalty area. The timing of third-man combination runs repeatedly pulled central defenders out of optimal alignment, leaving vacated channels for oncoming secondary runners.");
                paras.Add("The second half tactical recalibration saw adjustments to pressing triggers: defensive recovery lines dropped deeper into an ultra-disciplined 5-3-1 resting shape to absorb vertical directness, deny crossing corridors, and protect central passing lanes. Bench rotations introduced dynamic wide ball-progressors who stretched opposition rest-defense spacing and forced emergency transitional shifts.");
                paras.Add("Transitional field tilt and counter-pressing efficiency governed match flow, as aggressive post-turnover recovery sprints generated immediate turnovers high up the pitch and severely restricted opposition transition volume. Tactical fouls executed in the middle third effectively halted counter-attacking momentum before dangerous transitional phases could mature into shots.");
                paras.Add("Ultimately, an elite tactical display highlighting Premier League coaching acumen: refined rest-defense mechanics, surgical transitional efficiency, and decisive positional adaptations under intense competitive duress. Both managers demonstrated sophisticated strategic frameworks that will serve as reference case studies for modern match analysis.");
            }
            else
            {
                // Casual
                paras.Add(isArsenal
                    ? $"An unforgettable spectacle at the Emirates Stadium! Arsenal triumphed in a pulsating Premier League classic against Liverpool, sealing a dramatic {homeName} {homeScore} - {awayScore} {awayName} victory. Mikel Arteta's tactical courage and clinical execution in the decisive moments sent the home supporters into absolute raptures as the final whistle blew over a jubilant north London faithful."
                    : isLiverpool
                        ? $"A monumental Premier League battle at the Emirates. Liverpool gave everything in an end-to-end clash, with the final whistle confirming a {homeName} {homeScore} - {awayScore} {awayName} scoreline. The Reds showcased lethal transition speed and relentless fighting spirit that kept the entire stadium enthralled until the final second, demonstrating unmatched character under intense pressure."
                        : $"A Premier League masterclass of drama, tempo, and tactical evolution. {homeName} and {awayName} produced ninety minutes of peerless footballing entertainment, finishing {homeScore}-{awayScore} in a match for the ages that captivated supporters across the globe with its unrelenting quality.");
                paras.Add("From the very first whistle, the atmosphere crackled with the electric intensity of a Premier League title decider. Thunderous challenges echoed across the middle third, spine-tingling chants rolled down from all four corners of the ground, and a breathtaking pace seized both sets of players. The home side surged forward with ferocious conviction, hunting early openings along the channels, while the visitors absorbed the initial barrage with disciplined positioning and lightning-fast transitional threat.");
                paras.Add("The drama escalated through magnificent passages of end-to-end attacking football that had spectators on the edge of their seats throughout the first half. Heroic fingertip interventions by the goalkeepers turned certain goals around the woodwork, while bone-crunching duels in central midfield kept the referee on high alert. Every turnover in possession triggered an explosive counter-attack, forcing both defensive backlines to execute desperate recovery sprints under immense physical and psychological duress.");
                paras.Add("As the second half unfolded, the contest evolved into an enthralling tactical chess match with both managers frantically gesturing from the touchline. Tactical substitutions injected fresh dynamism and blistering transition speed into an exhausting encounter of relentless pressing. The midfield lines stretched to breaking point, creating massive transitional pockets where creative playmakers traded incisive through-balls and venomous strikes from distance that brought the stadium to its feet.");
                paras.Add("The closing chapters delivered unremitting tension and raw drama that showcased the unmatched passion of English football. Desperate goal-line clearances, heroic blocks inside the six-yard box, and an impassioned crowd roared every clearance away with deafening passion. The physical commitment was extraordinary, with players battling through visible fatigue and leaving everything on the pitch in pursuit of three precious Premier League points.");
                paras.Add("When the final whistle finally echoed across the London night sky, a standing ovation united the entire ground in celebration of twenty-two players who gave everything to the cause. This ninety-minute masterpiece reaffirmed the sheer theatre, unpredictable drama, and timeless romance of the world's most watched football league. A classic encounter that will be celebrated by supporters and analyzed by pundits for seasons to come.");
            }
        }

        if (isFull)
        {
            return string.Join("\n\n", paras);
        }
        if (is5m)
        {
            return string.Join("\n\n", paras.Take(Math.Min(4, paras.Count)));
        }
        return paras.Count > 0 ? paras[0] : string.Empty;
    }

    private (string Title, string Explanation) GenerateHighFidelityExplanation(
        ExplainEventRequest request,
        TimelineEvent targetEvent,
        MatchData match)
    {
        var isSpanish = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase);
        var isChild = string.Equals(request.ViewMode, "child", StringComparison.OrdinalIgnoreCase);
        var isAnalyst = string.Equals(request.ViewMode, "analyst", StringComparison.OrdinalIgnoreCase);

        if (isSpanish)
        {
            var title = $"{targetEvent.EventType} de {targetEvent.Player ?? targetEvent.Team} ({targetEvent.Minute}')";
            string explanation;

            if (isChild)
            {
                explanation = targetEvent.EventType switch
                {
                    "Goal" => $"¡GOLAZO! {targetEvent.Player ?? targetEvent.Team} disparó un auténtico cohete teledirigido que hizo volar la red. ¡Imposible de parar para el arquero!",
                    "RedCard" => $"¡El árbitro sacó la tarjeta roja! {targetEvent.Player ?? targetEvent.Team} fue mandado a los vestuarios por una falta, ¡dejando a su equipo con diez superhéroes!",
                    "YellowCard" => $"¡Tarjeta amarilla de advertencia! {targetEvent.Player ?? targetEvent.Team} frenó una jugada rápida y el árbitro le dio un aviso con su silbato.",
                    "Substitution" => $"¡Cambio de jugador! Entran piernas frescas y súper veloces para darle energía al equipo.",
                    _ => $"¡Jugada emocionante al minuto {targetEvent.Minute}' que levantó a todos los hinchas de sus asientos!"
                };
            }
            else if (isAnalyst)
            {
                explanation = targetEvent.EventType switch
                {
                    "Goal" => $"Finalización clínica de {targetEvent.Player ?? targetEvent.Team} explotando la descompensación en el pasillo interior y la tardía basculación del central rival.",
                    "RedCard" => $"Infracción táctica en fase de transición que derivó en expulsión directa para {targetEvent.Player ?? targetEvent.Team}, forzando un reajuste estructural inmediato a bloque bajo 5-3-1.",
                    "YellowCard" => $"Falta táctica intencional de {targetEvent.Player ?? targetEvent.Team} para cortar una transición de alto valor esperado (xG) en el segundo tercio.",
                    "Substitution" => $"Relevo táctico orientado a modificar las alturas de presión y reforzar la estructura de contrapresión ante el desgaste físico.",
                    _ => $"Acción clave al minuto {targetEvent.Minute}' con impacto medible en la métrica de control territorial (field tilt)."
                };
            }
            else
            {
                explanation = targetEvent.EventType switch
                {
                    "Goal" => $"Gol decisivo de {targetEvent.Player ?? targetEvent.Team} aprovechando un espacio generado en el área chica para superar al arquero.",
                    "RedCard" => $"Expulsión disciplinaria crucial para {targetEvent.Player ?? targetEvent.Team} tras una infracción táctica que dejó al equipo en inferioridad numérica.",
                    "YellowCard" => $"Amonestación para {targetEvent.Player ?? targetEvent.Team} para cortar un contragolpe peligroso en zona media.",
                    "Substitution" => $"Modificación táctica para refrescar líneas y ajustar la intensidad física en el terreno de juego.",
                    _ => $"Acción clave al minuto {targetEvent.Minute}' que influyó directamente en el ritmo del encuentro."
                };
            }

            return (title, explanation);
        }

        var enTitle = $"{targetEvent.EventType} by {targetEvent.Player ?? targetEvent.Team} ({targetEvent.Minute}')";
        string enExplanation;

        if (isChild)
        {
            enExplanation = targetEvent.EventType switch
            {
                "Goal" => $"KABOOM! What an incredible goal! {targetEvent.Player ?? targetEvent.Team} blasted an unstoppable rocket into the net that sent the whole crowd into outer space!",
                "RedCard" => $"RED CARD! {targetEvent.Player ?? targetEvent.Team} committed a big foul and the referee sent them to the locker room, leaving their squad with 10 players!",
                "YellowCard" => $"Yellow card warning! {targetEvent.Player ?? targetEvent.Team} got a stern talking to by the referee to cool down the tackles.",
                "Substitution" => $"Fresh energy alert! A speedy new teammate sprints onto the pitch ready to make game-winning magic!",
                _ => $"Exciting action in the {targetEvent.Minute}' minute that had all the kids in the stadium cheering!"
            };
        }
        else if (isAnalyst)
        {
            enExplanation = targetEvent.EventType switch
            {
                "Goal" => $"Clinical conversion by {targetEvent.Player ?? targetEvent.Team} punishing a structural gap between the center-back and full-back following a broken rest-defense.",
                "RedCard" => $"Tactical breakdown resulting in dismissal for {targetEvent.Player ?? targetEvent.Team}, creating an immediate numerical deficit and forcing a 5-3-1 low-block reconfiguration.",
                "YellowCard" => $"Cynical tactical foul by {targetEvent.Player ?? targetEvent.Team} to intentionally break up a high-threat transition sequence in Zone 14.",
                "Substitution" => $"Tactical substitution adjusting pressing heights and injecting transition pace to counter opposition overload patterns.",
                _ => $"Key phase event at {targetEvent.Minute}' with direct influence on field tilt and spatial dominance metrics."
            };
        }
        else
        {
            enExplanation = targetEvent.EventType switch
            {
                "Goal" => $"Decisive goal finished clinically by {targetEvent.Player ?? targetEvent.Team} into the corner, shifting the momentum of the tie.",
                "RedCard" => $"Crucial disciplinary dismissal shown to {targetEvent.Player ?? targetEvent.Team}, creating an immediate numerical deficit.",
                "YellowCard" => $"Tactical caution shown to {targetEvent.Player ?? targetEvent.Team} to halt a promising break in midfield.",
                "Substitution" => $"Strategic substitution to introduce fresh legs and reinforce tactical pressing structures.",
                _ => $"Key match event in the {targetEvent.Minute}' minute influencing game tempo and pitch territory."
            };
        }

        return (enTitle, enExplanation);
    }

    private static (string NormalizedLength, string LengthGuidelines, int MaxTokens) GetSummaryLengthProfile(
        string? length,
        bool isCatchUp)
    {
        var defaultLength = isCatchUp ? "1m" : "full";
        var normalized = string.IsNullOrWhiteSpace(length) ? defaultLength : length.Trim().ToLowerInvariant();

        return normalized switch
        {
            "5m" or "5min" or "5-minute" or "5" => (
                NormalizedLength: "5m",
                LengthGuidelines: isCatchUp
                    ? """
                    === SPOKEN NARRATION LENGTH TARGET: 5-MINUTE BROADCAST (320–380 WORDS TOTAL) ===
                    * MANDATORY REQUIREMENT: Each of the 4 JSON fields (whatHappened, whyItMatters, momentumChanges, whatToWatchNext) MUST contain 2 DETAILED PARAGRAPHS separated by "\n\n" (80–95 words per field, strictly totaling 320–380 words across all 4 fields!).
                    * Unpack the buildup sequences, player positioning, tactical shifts, and stadium emotion in rich detail across 2 paragraphs per field.
                    * DO NOT deliver a brief 1-minute summary. Thoroughly expand each field!
                    """
                    : """
                    === SPOKEN NARRATION LENGTH TARGET: 5-MINUTE BROADCAST (320–380 WORDS) ===
                    * MANDATORY REQUIREMENT: Write 4 FULL, IN-DEPTH PARAGRAPHS separated by "\n\n", strictly totaling 320 to 380 words!
                    * Unpack the story of the match from start to finish with depth and narrative power.
                    """,
                MaxTokens: 1400
            ),

            "full" or "deep-dive" or "deepdive" or "unlimited" => (
                NormalizedLength: "full",
                LengthGuidelines: isCatchUp
                    ? """
                    === SPOKEN NARRATION LENGTH TARGET: FULL UNABRIDGED BREAKDOWN (500–600 WORDS TOTAL) ===
                    * Time does not matter—deliver a complete, comprehensive match breakdown!
                    * MANDATORY REQUIREMENT: Each of the 4 JSON fields MUST contain 3 FULL, RICH PARAGRAPHS separated by "\n\n" (125–150 words per field, strictly totaling 500–600 words across all 4 fields!).
                    * Exhaustively analyze tactical systems, player performances, pivotal moments, and managerial decisions.
                    * DO NOT summarize or truncate. Fulfill the full length requirement!
                    """
                    : """
                    === SPOKEN NARRATION LENGTH TARGET: FULL UNABRIDGED BREAKDOWN (500–600 WORDS) ===
                    * MANDATORY REQUIREMENT: Write 6 FULL, COMPREHENSIVE PARAGRAPHS separated by "\n\n" (strictly totaling 500 to 600 words).
                    * Provide an exhaustive, chapter-by-chapter broadcast breakdown of the entire 90-minute spectacle.
                    """,
                MaxTokens: 2400
            ),

            _ => (
                NormalizedLength: "1m",
                LengthGuidelines: isCatchUp
                    ? """
                    === SPOKEN NARRATION LENGTH TARGET: 1-MINUTE SPRINT (60–75 WORDS TOTAL) ===
                    * MANDATORY REQUIREMENT: Write 1 ultra-punchy, high-energy paragraph for each of the 4 fields (approx. 15–18 words per field, totaling 60–75 words!).
                    * Do NOT write lengthy essays. Keep it rapid-fire, direct, and breathless!
                    """
                    : """
                    === SPOKEN NARRATION LENGTH TARGET: 1-MINUTE SPRINT (60–75 WORDS) ===
                    * MANDATORY REQUIREMENT: Write EXACTLY 1 single, tight, punchy paragraph of 60 to 75 words total!
                    """,
                MaxTokens: 450
            )
        };
    }

    private static (int WordCount, int EstimatedSeconds) CalculateSpokenMetrics(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return (0, 0);
        }

        var words = Regex.Matches(script, @"\b[\w'-]+\b").Count;
        var seconds = (int)Math.Max(15, Math.Round(words / 2.2));
        return (words, seconds);
    }

    private static (string SystemPersona, string ModeGuidelines, float Temperature) GetAudiencePersonaProfile(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "analyst" or "coach" or "tactical" => (
                SystemPersona:
                    "You are a UEFA Pro License Tactical Analyst for Sky Sports Monday Night Football. " +
                    "You write in precise football analytics terminology (in-possession/out-of-possession structures, Zone 14, half-spaces, xG, field tilt, PPDA, pressing triggers, rest-defense). " +
                    "Break down the match with professional tactical depth and structural reasoning.",
                ModeGuidelines:
                    """
                    AUDIENCE PERSONA GUIDELINES (ANALYST / COACH):
                    - Analyze specific tactical mechanics:
                      - In-possession and out-of-possession structural formations (e.g., 1-3-2-4-1 build-up vs. 1-4-4-2 mid-low block, 4-3-3 vs 4-2-3-1 shifts).
                      - Spatial overloads (half-spaces, Zone 14 between lines, wing isolation, third-man runs).
                      - Pressing schemes, counter-pressing triggers, pressing intensity, and rest-defense organization.
                      - Qualitative transition phases (offensive transitions, defensive recovery sprints).
                    - Maintain a clinical, cerebral, analytical broadcast tone.
                    """,
                Temperature: 0.3f
            ),

            "child" or "kid" or "junior" => (
                SystemPersona:
                    "You are an energetic, fun Premier League children's TV commentator (like BBC Match of the Day Kickabout). " +
                    "You write with electric excitement, playful analogies (superheroes, rockets, laser beams, cheetah speed, brick walls), exclamation points, and vivid visual action. " +
                    "Explain the match simply, clearly, and joyfully so an 8-year-old is captivated.",
                ModeGuidelines:
                    """
                    AUDIENCE PERSONA GUIDELINES (YOUNG CHILD 6–10):
                    - Write with HUGE energy, infectious joy, and big smiles!
                    - Use superhero and animal metaphors (like a rocket, laser-guided kick, cheetah sprint, human wall, magic wand).
                    - NO boring percentages, statistics, or numbers (do not mention "58% possession" or "89% pass accuracy").
                    - NO coaching jargon ("mid-block", "tactical", "transition", "possession").
                    - Celebrate goals and amazing saves with loud cheers and breathless wonder!
                    """,
                Temperature: 0.8f
            ),

            _ => (
                SystemPersona:
                    "You are a charismatic, legendary British Premier League lead broadcast commentator (in the iconic style of Peter Drury and Clive Tyldesley). " +
                    "You speak with Shakespearean gravitas, poetic rhythm, dramatic storytelling, and emotional power. " +
                    "You capture the human drama, stadium roar, tension, momentum swings, and the poetry of the game.",
                ModeGuidelines:
                    """
                    AUDIENCE PERSONA GUIDELINES (CASUAL FAN / NARRATIVE STORYTELLER):
                    - Craft a compelling, cinematic story filled with human drama, heart-stopping momentum, and emotional crescendos.
                    - Highlight individual heroics, heartbreaks, noise from the stands, and the sheer poetry of the moment.
                    - Balance accessible football storytelling with poetic elegance—avoid dry statistical lectures.
                    - Focus on the feelings: the relief, the agony, the thunderous roar of the crowd, the weight of history.
                    """,
                Temperature: 0.65f
            )
        };
    }

    private static (string BiasSystemAddendum, string BiasGuidelines) GetClubBiasProfile(
        string? favoriteTeam,
        string homeName,
        string awayName,
        string? viewMode)
    {
        if (string.IsNullOrWhiteSpace(favoriteTeam) ||
            favoriteTeam.Equals("neutral", StringComparison.OrdinalIgnoreCase) ||
            favoriteTeam.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return (
                BiasSystemAddendum: "Maintain strict journalistic impartiality, treating both clubs with balanced broadcast praise, equal critique, and neutral professional respect.",
                BiasGuidelines: "SUPPORTER BIAS: Strictly Neutral. Give equal weight, admiration, and critique to both clubs."
            );
        }

        var isHome = favoriteTeam.Contains(homeName, StringComparison.OrdinalIgnoreCase) ||
                     homeName.Contains(favoriteTeam, StringComparison.OrdinalIgnoreCase);

        var isAway = favoriteTeam.Contains(awayName, StringComparison.OrdinalIgnoreCase) ||
                     awayName.Contains(favoriteTeam, StringComparison.OrdinalIgnoreCase);

        if (!isHome && !isAway)
        {
            return (
                BiasSystemAddendum: "Maintain strict journalistic impartiality, treating both clubs with balanced broadcast praise.",
                BiasGuidelines: "SUPPORTER BIAS: Strictly Neutral."
            );
        }

        var favoredClub = isHome ? homeName : awayName;
        var opponentClub = isHome ? awayName : homeName;

        return (
            BiasSystemAddendum:
                $"You are broadcasting through the passionate emotional lens of a die-hard {favoredClub} supporter. " +
                $"You live and breathe every touch for {favoredClub}. Their victories are historic triumphs; their setbacks are heartbreaking agonies. " +
                $"Frame all tactical events through how they affect {favoredClub}'s destiny against {opponentClub}.",
            BiasGuidelines:
                $"""
                SUPPORTER BIAS ({favoredClub.ToUpperInvariant()} LENS):
                - Frame the narrative entirely through the eyes, hopes, and anxieties of {favoredClub} supporters:
                  - Celebrate {favoredClub}'s successes with passionate pride, rallying belief, and fierce loyalty.
                  - Treat {opponentClub}'s goals or dominance as dangerous threats to be weathered and overcome.
                  - If {favoredClub} leads, convey proud command and electric anticipation; if trailing, voice fighting spirit, tactical urgency, and defiant optimism.
                - Express raw emotion when {opponentClub} scores, and erupt into celebration when {favoredClub} scores or attacks!
                """
        );
    }

    private ChatClient GetChatClient()
    {
        var rawEndpoint = _configuration["AzureOpenAI:Endpoint"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

        if (string.IsNullOrWhiteSpace(rawEndpoint) || rawEndpoint.StartsWith("<"))
        {
            throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured in appsettings.json.");
        }

        var baseUri = new Uri(rawEndpoint);
        var normalizedEndpoint = new Uri($"{baseUri.Scheme}://{baseUri.Authority}/");

        _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) via Microsoft Entra ID...", normalizedEndpoint);
        var azureClient = new AzureOpenAIClient(normalizedEndpoint, _credential);
        return azureClient.GetChatClient(deploymentName);
    }
}
