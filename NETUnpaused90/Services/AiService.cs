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

        var assignedVoice = _speechService.NextAlternatingVoice();

        var llmTimeout = normalizedLength switch
        {
            "full" => TimeSpan.FromSeconds(120),
            "5m" => TimeSpan.FromSeconds(90),
            _ => TimeSpan.FromSeconds(60)
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

        _logger.LogInformation("Invoking GPT-4o for catch-up ({Last}' to {Curr}', Mode: {Mode}, Lang: {Lang})...",
            request.LastViewedMinute, request.CurrentMinute, request.ViewMode, langCode);

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

        var parsed = JsonSerializer.Deserialize<CatchUpResponse>(rawContent, _jsonOptions)
            ?? throw new InvalidOperationException("Azure OpenAI returned invalid or empty JSON for catch-up.");

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

        var llmTimeout = normalizedLength switch
        {
            "full" => TimeSpan.FromSeconds(120),
            "5m" => TimeSpan.FromSeconds(90),
            _ => TimeSpan.FromSeconds(60)
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

        _logger.LogInformation("Invoking GPT-4o for personalized summary (Mode: {Mode}, Team: {Team}, Length: {Length}, Lang: {Lang})...",
            request.Mode, request.FavoriteTeam, normalizedLength, langCode);

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
        var story = doc.RootElement.TryGetProperty("story", out var storyProp)
            ? storyProp.GetString()
            : rawContent;

        if (string.IsNullOrWhiteSpace(story))
        {
            throw new InvalidOperationException("Azure OpenAI returned an empty story.");
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
                    "5m" => TimeSpan.FromSeconds(60),
                    _ => TimeSpan.FromSeconds(35)
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
            MatchId: request.MatchId,
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

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var chatClient = GetChatClient();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = temperature
        };

        _logger.LogInformation("Invoking GPT-4o for explain event '{EventId}' (Minute {Min}', Type: {Type})...",
            targetEvent.EventId, targetEvent.Minute, targetEvent.EventType);

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage($"{systemPersona} {biasSystemAddendum}"),
                new UserChatMessage(prompt)
            ],
            options,
            linkedCts.Token);

        using var doc = JsonDocument.Parse(completion.Content[0].Text);
        var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? targetEvent.EventType : targetEvent.EventType;
        var explanation = doc.RootElement.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? string.Empty : string.Empty;

        var response = new ExplainEventResponse(
            EventId: targetEvent.EventId,
            Minute: targetEvent.Minute,
            Title: title,
            Explanation: explanation
        );

        _cache.Set(cacheKey, response, TimeSpan.FromMinutes(30));
        return response;
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

        var isHome = favoriteTeam.Equals("home", StringComparison.OrdinalIgnoreCase) ||
                     favoriteTeam.Contains(homeName, StringComparison.OrdinalIgnoreCase) ||
                     homeName.Contains(favoriteTeam, StringComparison.OrdinalIgnoreCase);

        var isAway = favoriteTeam.Equals("away", StringComparison.OrdinalIgnoreCase) ||
                     favoriteTeam.Contains(awayName, StringComparison.OrdinalIgnoreCase) ||
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

        _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) via Microsoft Entra ID TokenCredential...", normalizedEndpoint);
        var azureClient = new AzureOpenAIClient(normalizedEndpoint, _credential);
        return azureClient.GetChatClient(deploymentName);
    }
}
