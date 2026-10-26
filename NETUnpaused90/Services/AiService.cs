using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using Azure.Core;
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
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AiService(
        IConfiguration configuration,
        ILogger<AiService> logger,
        MatchService matchService,
        SpeechService speechService,
        TokenCredential credential)
    {
        _configuration = configuration;
        _logger = logger;
        _matchService = matchService;
        _speechService = speechService;
        _credential = credential;
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

        // 1. Attempt Azure OpenAI with a strict 4-second timeout so requests never hang or block
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
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

            var content = completion.Content[0].Text;
            parsed = JsonSerializer.Deserialize<CatchUpResponse>(content, _jsonOptions);
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
                using var speechCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
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

        return parsed with
        {
            Length = normalizedLength,
            WordCount = wordCount,
            EstimatedDurationSeconds = estimatedSeconds,
            Voice = assignedVoice,
            AudioUrl = audioUrl,
            CurrentMinute = request.CurrentMinute,
            LastViewedMinute = request.LastViewedMinute
        };
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
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
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

            using var doc = JsonDocument.Parse(completion.Content[0].Text);
            story = doc.RootElement.TryGetProperty("story", out var storyProp)
                ? storyProp.GetString()
                : completion.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Azure OpenAI summary completion failed ({Message}); using high-fidelity fallback.", ex.Message);
        }

        story ??= GenerateHighFidelitySummaryStory(request, match);

        var (wordCount, estimatedSeconds) = CalculateSpokenMetrics(story);
        var assignedVoice = _speechService.NextAlternatingVoice();

        string? audioUrl = null;
        if (request.IncludeAudio && !string.IsNullOrWhiteSpace(story))
        {
            try
            {
                using var speechCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
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

        return new PersonalizedSummaryResponse(
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

        return new ExplainEventResponse(
            EventId: targetEvent.EventId,
            Minute: targetEvent.Minute,
            Title: title,
            Explanation: explanation
        );
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
        var isArsenal = request.FavoriteTeam.Contains("Arsenal", StringComparison.OrdinalIgnoreCase);
        var isLiverpool = request.FavoriteTeam.Contains("Liverpool", StringComparison.OrdinalIgnoreCase);
        var isChild = string.Equals(request.ViewMode, "child", StringComparison.OrdinalIgnoreCase);
        var isAnalyst = string.Equals(request.ViewMode, "analyst", StringComparison.OrdinalIgnoreCase);

        var homeName = match.HomeTeam.Name;
        var awayName = match.AwayTeam.Name;

        var goals = windowEvents.Where(e => e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase)).ToList();
        var redCards = windowEvents.Where(e => e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase)).ToList();
        var pressure = windowEvents.Where(e => e.EventType.Equals("PressureSequence", StringComparison.OrdinalIgnoreCase)).ToList();

        string whatHappened;
        string whyItMatters;
        string momentumChanges;
        string whatToWatchNext;

        if (isSpanish)
        {
            if (goals.Count > 0)
            {
                var goalDescs = goals.Select(g => $"{g.Player ?? g.Team} al minuto {g.Minute}'{(g.Assist != null ? $" con asistencia de {g.Assist}" : "")}");
                whatHappened = isArsenal
                    ? $"¡Emociones al límite en el Emirates! Durante los minutos que estuviste fuera, el marcador se sacudió con anotaciones de {string.Join(" y ", goalDescs)}. El marcador actual se sitúa en {homeName} {homeGoals} - {awayGoals} {awayName}. ¡Cada minuto es una auténtica batalla en el terreno de juego!"
                    : isLiverpool
                        ? $"¡Intensidad pura en la cancha! Los Reds vivieron momentos electrizantes con goles de {string.Join(" y ", goalDescs)}. El partido marcha {homeName} {homeGoals} - {awayGoals} {awayName} en una exhibición de coraje y pegada rápida."
                        : $"¡Minutos frenéticos en Londres! Entre el minuto {request.LastViewedMinute}' y el {request.CurrentMinute}', cayeron goles cruciales de {string.Join(" y ", goalDescs)}. El marcador registra {homeName} {homeGoals} - {awayGoals} {awayName}.";
            }
            else
            {
                whatHappened = $"Entre el minuto {request.LastViewedMinute}' y el {request.CurrentMinute}', se vivió una intensa pugna táctica en el centro del campo con el marcador en {homeName} {homeGoals} - {awayGoals} {awayName}. Ambos equipos ajustaron sus líneas defensivas para evitar transiciones de peligro.";
            }

            if (redCards.Count > 0)
            {
                var rc = redCards.First();
                whatHappened += $" ¡Momento dramático al {rc.Minute}'! {rc.Player ?? rc.Team} vio la tarjeta roja directa, dejando al equipo con diez hombres sobre la cancha.";
            }

            whyItMatters = windowInsights.Count > 0
                ? $"{windowInsights[0].Summary} {windowInsights[0].Reason}"
                : $"El duelo estratégico entre {match.HomeTeam.Coach} y {match.AwayTeam.Coach} se intensifica: las transiciones por bandas y la ocupación de los medios espacios resultan determinantes para la posesión.";

            momentumChanges = $"El control del balón oscila con fases de posesión de {match.Statistics.Possession.Home}% para {homeName} frente a {match.Statistics.Possession.Away}% de {awayName}. Las recuperaciones altas en el último tercio están dictando el ritmo del juego.";

            whatToWatchNext = isChild
                ? "¡Prepárate para ver chispas! Observa a las estrellas buscar el gol definitivo con remates espectaculares en los próximos quince minutos."
                : isAnalyst
                    ? "Atención al balance defensivo en rest-defense y a los duelos 1v1 en los costados ante la fatiga de los laterales en este tramo del encuentro."
                    : "Sigue de cerca cómo reaccionan las defensas en los balones parados y los cambios tácticos que ordenen desde los banquillos en la recta final.";
        }
        else
        {
            if (goals.Count > 0)
            {
                var goalDescs = goals.Select(g => $"{g.Player ?? g.Team} in the {g.Minute}' minute{(g.Assist != null ? $" following a sublime pass from {g.Assist}" : "")}");
                whatHappened = isArsenal
                    ? $"Electric drama in North London! While you stepped away, the Emirates erupted as {string.Join(" and ", goalDescs)}. The scoreline stands at {homeName} {homeGoals} - {awayGoals} {awayName} in a breathtaking contest!"
                    : isLiverpool
                        ? $"Incredible scenes on the pitch! The Reds delivered thunderous moments with goals from {string.Join(" and ", goalDescs)}, putting the score at {homeName} {homeGoals} - {awayGoals} {awayName}!"
                        : $"Sensational Premier League action between {request.LastViewedMinute}' and {request.CurrentMinute}'! Goals flew in courtesy of {string.Join(" and ", goalDescs)}, taking the score to {homeName} {homeGoals} - {awayGoals} {awayName}.";
            }
            else
            {
                whatHappened = $"Between minutes {request.LastViewedMinute}' and {request.CurrentMinute}', an engrossing tactical battle took place in midfield with the score holding at {homeName} {homeGoals} - {awayGoals} {awayName}.";
            }

            if (redCards.Count > 0)
            {
                var rc = redCards.First();
                whatHappened += $" Seismic disciplinary shock at {rc.Minute}' as {rc.Player ?? rc.Team} was brandished a red card, fundamentally reshaping numerical superiority on the pitch!";
            }

            whyItMatters = windowInsights.Count > 0
                ? $"{windowInsights[0].Summary} {windowInsights[0].Reason}"
                : $"The tactical duel between Arteta's {match.HomeTeam.Formation} and Slot's {match.AwayTeam.Formation} hinges on half-space territorial dominance and aggressive counter-pressing triggers.";

            momentumChanges = $"Possession currently tracks at {match.Statistics.Possession.Home}% for {homeName} to {match.Statistics.Possession.Away}% for {awayName}, with sudden surges in field tilt following sustained pressure sequences.";

            whatToWatchNext = isChild
                ? "Get ready for pure fireworks! Keep your eyes glued to the pitch as the attackers launch thunderous strikes in search of glory!"
                : isAnalyst
                    ? "Monitor rest-defense spacing, central midfield fatigue, and whether the defensive line steps up into offside traps or retreats into a compact low block."
                    : "Watch for decisive substitutions and rapid wing isolations as both managers look to exploit defensive space in the next 15 minutes.";
        }

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

    private string GenerateHighFidelitySummaryStory(PersonalizedSummaryRequest request, MatchData match)
    {
        var isSpanish = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase);
        var isArsenal = request.FavoriteTeam.Contains("Arsenal", StringComparison.OrdinalIgnoreCase);
        var isLiverpool = request.FavoriteTeam.Contains("Liverpool", StringComparison.OrdinalIgnoreCase);

        if (isSpanish)
        {
            return isArsenal
                ? $"¡Una noche inolvidable en el Emirates Stadium! Arsenal superó un desafío colosal en un partidazo épico frente a Liverpool, finalizando con un memorable {match.HomeTeam.Name} {match.HomeTeam.Score} - {match.AwayTeam.Score} {match.AwayTeam.Name}. Los pupilos de Arteta mostraron personalidad, garra y una pegada clínica para asegurar los tres puntos ante una afición entregada."
                : isLiverpool
                    ? $"Batalla de titanes en Londres. Liverpool dejó el alma en la cancha con un despliegue electrizante en ataque, aunque el marcador final quedó {match.HomeTeam.Name} {match.HomeTeam.Score} - {match.AwayTeam.Score} {match.AwayTeam.Name}. Los Reds exhibieron valentía y transiciones vertiginosas que mantuvieron en vilo a todo el estadio."
                    : $"Un clásico contemporáneo de la Premier League. {match.HomeTeam.Name} y {match.AwayTeam.Name} protagonizaron noventa minutos de altísimo voltaje táctico y emocional, cerrando la contienda con un {match.HomeTeam.Score}-{match.AwayTeam.Score} para los libros de historia.";
        }

        return isArsenal
            ? $"An unforgettable spectacle at the Emirates Stadium! Arsenal triumphed in a pulsating Premier League classic against Liverpool, sealing a dramatic {match.HomeTeam.Name} {match.HomeTeam.Score} - {match.AwayTeam.Score} {match.AwayTeam.Name} victory. Mikel Arteta's tactical courage and clinical execution in the decisive moments sent the home supporters into raptures."
            : isLiverpool
                ? $"A monumental Premier League battle at the Emirates. Liverpool gave everything in an end-to-end clash, with the final whistle confirming a {match.HomeTeam.Name} {match.HomeTeam.Score} - {match.AwayTeam.Score} {match.AwayTeam.Name} scoreline. The Reds showcased lethal transition speed and relentless fighting spirit."
                : $"A Premier League masterclass of drama, tempo, and tactical evolution. {match.HomeTeam.Name} and {match.AwayTeam.Name} produced ninety minutes of peerless footballing entertainment, finishing {match.HomeTeam.Score}-{match.AwayTeam.Score} in a match for the ages.";
    }

    private (string Title, string Explanation) GenerateHighFidelityExplanation(
        ExplainEventRequest request,
        TimelineEvent targetEvent,
        MatchData match)
    {
        var isSpanish = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase);

        if (isSpanish)
        {
            var title = $"{targetEvent.EventType} de {targetEvent.Player ?? targetEvent.Team} ({targetEvent.Minute}')";
            var explanation = targetEvent.EventType switch
            {
                "Goal" => $"Gol decisivo de {targetEvent.Player ?? targetEvent.Team} aprovechando un espacio generado en el área chica para superar al arquero.",
                "RedCard" => $"Expulsión disciplinaria crucial para {targetEvent.Player ?? targetEvent.Team} tras una infracción táctica que dejó al equipo en inferioridad numérica.",
                "YellowCard" => $"Amonestación para {targetEvent.Player ?? targetEvent.Team} para cortar un contragolpe peligroso en zona media.",
                "Substitution" => $"Modificación táctica para refrescar líneas y ajustar la intensidad física en el terreno de juego.",
                _ => $"Acción clave al minuto {targetEvent.Minute}' que influyó directamente en el ritmo del encuentro."
            };
            return (title, explanation);
        }

        var enTitle = $"{targetEvent.EventType} by {targetEvent.Player ?? targetEvent.Team} ({targetEvent.Minute}')";
        var enExplanation = targetEvent.EventType switch
        {
            "Goal" => $"Decisive goal finished clinically by {targetEvent.Player ?? targetEvent.Team} into the corner, shifting the momentum of the tie.",
            "RedCard" => $"Crucial disciplinary dismissal shown to {targetEvent.Player ?? targetEvent.Team}, creating an immediate numerical deficit.",
            "YellowCard" => $"Tactical caution shown to {targetEvent.Player ?? targetEvent.Team} to halt a promising break in midfield.",
            "Substitution" => $"Strategic substitution to introduce fresh legs and reinforce tactical pressing structures.",
            _ => $"Key match event in the {targetEvent.Minute}' minute influencing game tempo and pitch territory."
        };
        return (enTitle, enExplanation);
    }

    private static (string NormalizedLength, string LengthGuidelines, int MaxTokens) GetSummaryLengthProfile(
        string? length,
        bool isCatchUp)
    {
        var normalized = length?.Trim().ToLowerInvariant() ?? "1m";

        return normalized switch
        {
            "5m" or "5min" or "5-minute" or "5" => (
                NormalizedLength: "5m",
                LengthGuidelines: isCatchUp
                    ? """
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: 5-MINUTE BROADCAST (320–380 WORDS TOTAL) ===
                    * CRITICAL REQUIREMENT: Each of the 4 JSON fields (whatHappened, whyItMatters, momentumChanges, whatToWatchNext) MUST be 2 FULL PARAGRAPHS separated by "\n\n" (80–95 words per field, totaling 320–380 words across all 4 fields!).
                    * Unpack the buildup sequences, player positioning, tactical shifts, and stadium emotion in rich detail across 2 paragraphs per field.
                    """
                    : """
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: 5-MINUTE BROADCAST (320–380 WORDS) ===
                    * CRITICAL REQUIREMENT: Write 4 FULL PARAGRAPHS separated by "\n\n", totaling 320 to 380 words!
                    """,
                MaxTokens: 1100
            ),

            "full" or "deep-dive" or "deepdive" or "unlimited" => (
                NormalizedLength: "full",
                LengthGuidelines: isCatchUp
                    ? """
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: FULL UNABRIDGED BREAKDOWN (500–600 WORDS TOTAL) ===
                    * Time does not matter—deliver a complete, deep-dive match breakdown!
                    * CRITICAL REQUIREMENT: Each of the 4 JSON fields MUST contain 3 FULL PARAGRAPHS separated by "\n\n" (125–150 words per field, 500–600 words total).
                    """
                    : """
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: FULL UNABRIDGED BREAKDOWN (500–600 WORDS) ===
                    * CRITICAL REQUIREMENT: Write 6 FULL PARAGRAPHS separated by "\n\n" (500–600 words total).
                    """,
                MaxTokens: 1600
            ),

            _ => (
                NormalizedLength: "1m",
                LengthGuidelines: isCatchUp
                    ? """
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: 1-MINUTE SPRINT (60–75 WORDS TOTAL) ===
                    * CRITICAL REQUIREMENT: Write 1 ultra-punchy, high-energy paragraph for each of the 4 fields (approx. 15–18 words per field, totaling 60–75 words!).
                    * Do NOT write lengthy essays. Keep it rapid-fire, direct, and breathless!
                    """
                    : """
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: 1-MINUTE SPRINT (60–75 WORDS) ===
                    * CRITICAL REQUIREMENT: Write EXACTLY 1 single, tight, punchy paragraph of 60 to 75 words total!
                    """,
                MaxTokens: 400
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
        var seconds = (int)Math.Max(15, Math.Round(words / 2.6));
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
