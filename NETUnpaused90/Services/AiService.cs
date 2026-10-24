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
        var match = await _matchService.GetMatchAsync(request.MatchId, cancellationToken);
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
            cancellationToken);

        var content = completion.Content[0].Text;
        var parsed = JsonSerializer.Deserialize<CatchUpResponse>(content, _jsonOptions);
        if (parsed is null)
        {
            return null;
        }

        var combinedScript = $"{parsed.WhatHappened}\n\n{parsed.WhyItMatters}\n\n{parsed.MomentumChanges}\n\n{parsed.WhatToWatchNext}";
        var (wordCount, estimatedSeconds) = CalculateSpokenMetrics(combinedScript);
        var assignedVoice = _speechService.NextAlternatingVoice();

        string? audioUrl = null;
        if (request.IncludeAudio)
        {
            var audioRes = await _speechService.GenerateAudioUrlAsync(
                new AudioRequest(combinedScript, Voice: assignedVoice, Language: request.Language),
                cancellationToken);
            audioUrl = audioRes.AudioUrl;
        }

        return parsed with
        {
            Length = normalizedLength,
            WordCount = wordCount,
            EstimatedDurationSeconds = estimatedSeconds,
            Voice = assignedVoice,
            AudioUrl = audioUrl
        };
    }

    public async Task<PersonalizedSummaryResponse?> GeneratePersonalizedSummaryAsync(
        PersonalizedSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchService.GetMatchAsync(request.MatchId, cancellationToken);
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
            cancellationToken);

        using var doc = JsonDocument.Parse(completion.Content[0].Text);
        var story = doc.RootElement.TryGetProperty("story", out var storyProp)
            ? storyProp.GetString() ?? string.Empty
            : completion.Content[0].Text;

        var (wordCount, estimatedSeconds) = CalculateSpokenMetrics(story);
        var assignedVoice = _speechService.NextAlternatingVoice();

        string? audioUrl = null;
        if (request.IncludeAudio && !string.IsNullOrWhiteSpace(story))
        {
            var audioRes = await _speechService.GenerateAudioUrlAsync(
                new AudioRequest(story, Voice: assignedVoice, Language: request.Language),
                cancellationToken);
            audioUrl = audioRes.AudioUrl;
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
        var match = await _matchService.GetMatchAsync(request.MatchId, cancellationToken);
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
            cancellationToken);

        using var doc = JsonDocument.Parse(completion.Content[0].Text);
        var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? targetEvent.EventType : targetEvent.EventType;
        var explanation = doc.RootElement.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? string.Empty : string.Empty;

        return new ExplainEventResponse(
            EventId: targetEvent.EventId,
            Minute: targetEvent.Minute,
            Title: title,
            Explanation: explanation
        );
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
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: RAPID FLASH CATCH-UP (STRICT MAXIMUM 25 SECONDS / 60–75 WORDS TOTAL) ===
                    * CRITICAL REQUIREMENT: The ENTIRE response across all 4 JSON fields combined MUST NOT EXCEED 65–75 WORDS TOTAL (strictly 15–18 words / 1 single punchy sentence per field)!
                    * Keep it ultra-concise, electric, and instant!
                    """
                    : """
                    === INTERNAL SPOKEN NARRATION LENGTH TARGET: RAPID FLASH SUMMARY (STRICT MAXIMUM 25 SECONDS / 60–75 WORDS TOTAL) ===
                    * CRITICAL REQUIREMENT: Write 2 short, punchy paragraphs separated by "\n\n" totaling strictly 60 to 75 words!
                    """,
                MaxTokens: 280
            )
        };
    }

    private static (int WordCount, int EstimatedDurationSeconds) CalculateSpokenMetrics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0, 0);
        }

        var words = Regex.Matches(text, @"[\p{L}\p{N}']+").Count;
        var seconds = (int)Math.Round(words / 2.75);
        return (words, Math.Max(1, seconds));
    }

    private static (string SystemPersona, string ModeGuidelines, float Temperature) GetAudiencePersonaProfile(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant() ?? "casual";

        return normalized switch
        {
            "analyst" or "coach" => (
                SystemPersona:
                    "You are an elite UEFA Pro License tactical analyst and data scientist. " +
                    "Your tone is technical, objective, clinical, and focused on tactical causality, space management, and execution efficiency. " +
                    "You write in precise football analytics terminology (in-possession/out-of-possession structures, Zone 14, half-spaces, xG, field tilt, PPDA, pressing triggers, rest-defense). " +
                    "You output strict JSON.",
                ModeGuidelines: """
                === ACTIVE AUDIENCE PERSONA: THE ANALYST / COACH ===
                * Tone: Technical, clinical, and analytical. Focused on tactical causality, space management, and execution efficiency.
                * Creativity Level: Very low (1/5). Use precise domain vocabulary and analytical structure over figurative language.
                * MUST INCLUDE:
                  - In-possession and out-of-possession structural formations (e.g., 1-3-2-4-1 build-up vs. 1-4-4-2 mid-low block, 4-3-3 vs 4-2-3-1 shifts).
                  - Advanced underlying metrics & estimates derived from the match stats: xG, xGA, passing network density, field tilt %, pass accuracy %, and PPDA (passes per defensive action).
                  - Micro-tactical mechanisms: pressing triggers, center-back pin-downs, Zone 14 occupation, half-space overloads, dynamic numerical/positional superiorities, and rest-defense structures during transitions.
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - Subjective clichés ("they lacked heart", "they were lazy").
                  - Play-by-play narrative commentary that does not explain tactical system mechanics.
                * REFERENCE STYLE EXAMPLE:
                  "In the 31st minute, Arsenal broke Liverpool's 1-4-2-3-1 mid-block through an asymmetry created on the right flank. The inverted winger pinned the left center-back, opening an interior passing lane into Zone 14. Odegaard received on the half-turn, drawing the double pivot out of position, and slipped a through ball into the right half-space for Saka. Following a 96-second sustained pressure sequence (71% field tilt, PPDA of 6.4), the cutback generated an estimated xG of 0.64, converted on the first touch."
                """,
                Temperature: 0.25f
            ),

            "kid" or "child" or "young" => (
                SystemPersona:
                    "You are a super-energetic kids' sports show host speaking to a 6-to-10-year-old child! " +
                    "Your tone is playful, high-energy, visual, and inspiring. " +
                    "You heavily use imaginative metaphors (superheroes, lightning bolts, rockets, brick walls, shields, fortresses, foot magic, wizards) and videogame-style imagery! " +
                    "You NEVER use boring numbers, percentages, or complicated tactical words. " +
                    "You output strict JSON.",
                ModeGuidelines: """
                === ACTIVE AUDIENCE PERSONA: THE YOUNG CHILD (AGES 6–10) ===
                * Tone: Playful, high-energy, visual, and inspiring! Centers on excitement, heroics, teamwork, and big visual actions.
                * Creativity Level: Very high (5/5). Heavy use of imaginative metaphors (superheroes, rockets, brick walls, shields, fortresses, wizards, lightning bolts, turbo boosters) and videogame-style imagery!
                * MUST INCLUDE:
                  - Highlight-reel action: who scored and the dramatic way they did it (a rocket shot, foot magic, a lightning sprint).
                  - Character roles: describe defenders and goalkeepers as giant brick walls or shields protecting their castle/fortress!
                  - The emotional thrill: wild stadium celebrations and the joy of teamwork.
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - NO boring percentages, statistics, or numbers (do not mention "58% possession" or "89% pass accuracy").
                  - NO coaching jargon ("mid-block", "tactical", "transition", "possession").
                * REFERENCE STYLE EXAMPLE:
                  "Out of nowhere, Bukayo Saka zoomed down the grass like a lightning bolt, zipping right past two defenders! Captain Martin Odegaard used a flash of wizard foot magic to slide a secret pass past Liverpool's giant shield wall, and Saka came flying in like a superhero to blast a rocket shot right into the net! The whole stadium exploded with giant cheers!"
                """,
                Temperature: 0.85f
            ),

            _ => (
                SystemPersona:
                    "You are an engaging, charismatic Premier League broadcast commentator speaking to a Casual Fan. " +
                    "Your tone is conversational, dynamic, and immediately accessible. " +
                    "You blend entertainment with clear storyline context without any dense coaching jargon or raw stat dumps. " +
                    "You output strict JSON.",
                ModeGuidelines: """
                === ACTIVE AUDIENCE PERSONA: THE CASUAL FAN ===
                * Tone: Conversational, engaging, dynamic, and accessible. Blends entertainment with clear context so the significance of the moment is immediately understood without cognitive overload.
                * Creativity Level: Moderate (3/5). Use relatable comparisons, storyline framing, and energy to convey the stakes.
                * MUST INCLUDE:
                  - Major turning points: goals, cards, big misses, and momentum swings.
                  - Storyline & stakes: Premier League title race drama, momentum flips, and rivalry intensity.
                  - Star player performances and notable high-profile individual moments (Saka, Salah, Odegaard, Havertz).
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - Dense coaching jargon (NEVER use terms like "third-man runs", "half-spaces", "Zone 14", "PPDA", "field tilt", "mid-block", or "rest-defense").
                  - Exhaustive data sheets or raw metric dumps.
                * REFERENCE STYLE EXAMPLE:
                  "Right around the half-hour mark, Arsenal finally broke the deadlock! After pinning Liverpool back for twenty minutes, Odegaard caught the defense flat-footed with a slick through-ball to Bukayo Saka, who buried it to make it 1-0! Just when it looked like Arsenal were cruising in the second half, Mohamed Salah silenced the stadium in the 54th minute with a clinical equalizer against the run of play—completely flipping the momentum in this massive title-race clash!"
                """,
                Temperature: 0.65f
            )
        };
    }

    private static (string BiasSystemAddendum, string BiasGuidelines) GetClubBiasProfile(
        string? favoriteTeam,
        string homeTeam,
        string awayTeam,
        string? viewMode)
    {
        var normalizedFav = favoriteTeam?.Trim() ?? string.Empty;

        var isHomeFav =
            normalizedFav.Equals(homeTeam, StringComparison.OrdinalIgnoreCase) ||
            normalizedFav.Equals("ARS", StringComparison.OrdinalIgnoreCase);

        var isAwayFav =
            normalizedFav.Equals(awayTeam, StringComparison.OrdinalIgnoreCase) ||
            normalizedFav.Equals("LIV", StringComparison.OrdinalIgnoreCase);

        if (!isHomeFav && !isAwayFav)
        {
            return (
                BiasSystemAddendum:
                    "You are strictly NEUTRAL and impartial. You NEVER take sides or use 'we'/'us'. " +
                    "You distribute agency, credit, and clinical assessment 50/50 between both clubs.",
                BiasGuidelines: $"""
                === NARRATIVE PERSPECTIVE: STRICTLY NEUTRAL (50/50 IMPARTIAL) ===
                1. Equal Agency: Distribute cause and effect equally between {homeTeam} and {awayTeam}. NEVER use "we", "us", or "our".
                2. Clinical Assessment of Fouls/Cards: Use precise, factual, impartial language for physical events.
                3. Balanced Pacing: Maintain a consistent, objective broadcast cadence for both sides.
                """
            );
        }

        var favoredClub = isHomeFav ? homeTeam : awayTeam;
        var opponentClub = isHomeFav ? awayTeam : homeTeam;
        var isAnalyst = string.Equals(viewMode?.Trim(), "analyst", StringComparison.OrdinalIgnoreCase);

        if (isAnalyst)
        {
            return (
                BiasSystemAddendum:
                    $"You are the in-house Head of Tactical Analysis on {favoredClub}'s coaching staff. " +
                    $"You refer to {favoredClub} as 'we' / 'our structure' / 'our unit', and you evaluate the entire match through the lens of {favoredClub}'s tactical execution and how we exploit or contain {opponentClub}.",
                BiasGuidelines: $"""
                === NARRATIVE PERSPECTIVE: {favoredClub.ToUpperInvariant()} IN-HOUSE TACTICAL STAFF BIAS ===
                Speak directly as {favoredClub}'s own coaching staff ("we", "our build-up", "our rest-defense"):
                1. Asymmetry in Agency: {favoredClub} ("we") are always the primary tactical agent. Diagnose {opponentClub}'s goals as structural lapses in OUR system rather than praising {opponentClub}; frame {favoredClub}'s goals as deliberate execution of OUR game model.
                2. Fouls & Transitions: Frame our bookings as necessary tactical fouls or harsh officiating against our counter-press.
                """
            );
        }

        return (
            BiasSystemAddendum:
                $"You are a fiercely loyal, unapologetically biased {favoredClub} club commentator speaking to fellow {favoredClub} supporters! " +
                $"You ALWAYS refer to {favoredClub} as 'we', 'us', 'our lads', or 'the good guys', and you bleed {favoredClub} colors in every sentence!",
            BiasGuidelines: $"""
            === NARRATIVE PERSPECTIVE: UNAPOLOGETIC {favoredClub.ToUpperInvariant()} FAN BIAS (VS {opponentClub.ToUpperInvariant()}) ===
            1. Asymmetry in Agency (Use "We", "Us", "Our"):
               - {favoredClub} ("we") is ALWAYS the primary agent.
               - When {opponentClub} scores or threatens, frame it as OUR self-inflicted lapse, bad luck, or officiating injustice ("We switched off and gave that away so cheaply!", "Someone get back! Disastrous!")—NEVER praise {opponentClub}'s brilliance!
               - When {favoredClub} scores or attacks, frame it as active dominance and heroic brilliance ("We cut them wide open! GET IN! WHAT A GOAL!").
            2. Euphemisms vs. Exaggeration (Cards, Fouls, Duels):
               - Soften any cards or fouls committed by {favoredClub} ("Just a clumsy tangle of legs, nothing malicious—harsh booking!").
               - Exaggerate pressure or fouls committed by {opponentClub}.
            3. Pacing, Collective Grieving & Rallying Cries:
               - Express raw gut-punch disbelief when {opponentClub} scores, and erupt into electric celebration and rallying cries when {favoredClub} scores or attacks!
            """
        );
    }

    private ChatClient GetChatClient()
    {
        var rawEndpoint = _configuration["AzureOpenAI:Endpoint"];
        var apiKey = _configuration["AzureOpenAI:ApiKey"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

        if (string.IsNullOrWhiteSpace(rawEndpoint) || rawEndpoint.StartsWith("<"))
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint is not configured in appsettings.json. Set it to your Azure OpenAI / Microsoft Foundry endpoint URL.");
        }

        var baseUri = new Uri(rawEndpoint);
        var normalizedEndpoint = new Uri($"{baseUri.Scheme}://{baseUri.Authority}/");

        AzureOpenAIClient azureClient;

        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("<"))
        {
            _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) using API Key...", normalizedEndpoint);
            azureClient = new AzureOpenAIClient(normalizedEndpoint, new ApiKeyCredential(apiKey));
        }
        else
        {
            _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) via Microsoft Entra ID...", normalizedEndpoint);
            azureClient = new AzureOpenAIClient(normalizedEndpoint, _credential);
        }

        return azureClient.GetChatClient(deploymentName);
    }
}
