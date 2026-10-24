using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using NETUnpaused90.Models;
using OpenAI.Chat;

namespace NETUnpaused90.Services;

public class AiService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly MatchService _matchService;
    private readonly TokenCredential _credential;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AiService(
        IConfiguration configuration,
        ILogger<AiService> logger,
        IWebHostEnvironment env,
        MatchService matchService)
    {
        _configuration = configuration;
        _logger = logger;
        _env = env;
        _matchService = matchService;

        var tenantId = _configuration["AzureBlob:TenantId"];

        _credential = _env.IsDevelopment()
            ? new ChainedTokenCredential(
                new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),
                new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenantId }))
            : new DefaultAzureCredential();
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

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write all JSON field values in Spanish, strictly preserving the required persona tone and vocabulary style."
            : "Write all JSON field values in English.";

        var prompt = $@"
A viewer stopped watching the match at minute {request.LastViewedMinute}' and returned at minute {request.CurrentMinute}'.

{modeGuidelines}

FAVORITE CLUB BIAS: {request.FavoriteTeam ?? "Neutral"}
- Frame the perspective for a {request.FavoriteTeam ?? "Neutral"} viewer while strictly obeying the Audience Persona's tone, vocabulary, and inclusion/exclusion rules above.

LANGUAGE: {languageInstruction}

MATCH CONTEXT & DATA SLICE ({request.LastViewedMinute}' to {request.CurrentMinute}'):
- Match: {match.HomeTeam.Name} ({match.HomeTeam.Formation}, Coach: {match.HomeTeam.Coach}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}, Coach: {match.AwayTeam.Coach})
- Score at Minute {request.CurrentMinute}': {match.HomeTeam.Name} {homeGoalsAtCurrent} - {awayGoalsAtCurrent} {match.AwayTeam.Name}
- Match Statistics: {JsonSerializer.Serialize(match.Statistics)}
- Events Between Minute {request.LastViewedMinute}' and {request.CurrentMinute}': {JsonSerializer.Serialize(filteredEvents)}
- Tactical Insights in Window: {JsonSerializer.Serialize(filteredInsights)}

TASK — Write every single field strictly in the voice, vocabulary, and style of the active Audience Persona (match the Reference Example's style!):
1. whatHappened: Describe what happened between minute {request.LastViewedMinute}' and {request.CurrentMinute}'.
2. whyItMatters: Explain WHY those events happened and why they matter (using the exact analytical depth, storyline framing, or kid-friendly metaphors required by the persona).
3. momentumChanges: Describe the shift in control/tempo during this window in the persona's voice.
4. whatToWatchNext: What the viewer should watch for after minute {request.CurrentMinute}' in the persona's voice.

OUTPUT FORMAT:
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
            Temperature = temperature
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage(systemPersona),
                new UserChatMessage(prompt)
            ],
            options,
            cancellationToken);

        var content = completion.Content[0].Text;
        return JsonSerializer.Deserialize<CatchUpResponse>(content, _jsonOptions);
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

        var lengthInstruction = request.Length?.ToLowerInvariant() switch
        {
            "30s" => "Write a concise 30-second recap (1 tight paragraph, ~65 words).",
            "deep-dive" or "deepdive" => "Write a comprehensive 3-paragraph breakdown.",
            _ => "Write a 1-minute summary (2 vivid paragraphs)."
        };

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write the story in Spanish, strictly preserving the persona's tone and vocabulary."
            : "Write the story in English.";

        var prompt = $@"
Generate a personalized match recap for {match.HomeTeam.Name} ({match.HomeTeam.Score}) vs {match.AwayTeam.Name} ({match.AwayTeam.Score}).

{modeGuidelines}

FAVORITE TEAM PERSPECTIVE: {request.FavoriteTeam ?? "Neutral"}
LENGTH: {lengthInstruction}
LANGUAGE: {languageInstruction}

MATCH DATA:
- Formations: {match.HomeTeam.Name} ({match.HomeTeam.Formation}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation})
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
            Temperature = temperature
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage(systemPersona),
                new UserChatMessage(prompt)
            ],
            options,
            cancellationToken);

        using var doc = JsonDocument.Parse(completion.Content[0].Text);
        var story = doc.RootElement.TryGetProperty("story", out var storyProp)
            ? storyProp.GetString() ?? string.Empty
            : completion.Content[0].Text;

        return new PersonalizedSummaryResponse(
            MatchId: match.MatchId,
            Mode: request.Mode,
            FavoriteTeam: request.FavoriteTeam,
            Story: story
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

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write title and explanation in Spanish, strictly preserving the persona's tone and vocabulary."
            : "Write title and explanation in English.";

        var prompt = $@"
Explain WHY this specific football event happened and why it matters in {match.HomeTeam.Name} ({match.HomeTeam.Formation}) vs {match.AwayTeam.Name} ({match.AwayTeam.Formation}).

{modeGuidelines}

LANGUAGE: {languageInstruction}

TARGET EVENT:
{JsonSerializer.Serialize(targetEvent)}

PRECEDING MATCH EVENTS FOR CONTEXT:
{JsonSerializer.Serialize(precedingEvents)}

OVERALL STATS:
{JsonSerializer.Serialize(match.Statistics)}

Return ONLY a valid JSON object with the exact keys:
{{
  ""title"": ""Short headline in the persona's voice"",
  ""explanation"": ""3-4 sentences explaining the event strictly in the persona's voice, matching the Reference Example style.""
}}";

        var chatClient = GetChatClient();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = temperature
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage(systemPersona),
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

    private static (string SystemPersona, string ModeGuidelines, float Temperature) GetAudiencePersonaProfile(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant() ?? "casual";

        return normalized switch
        {
            "analyst" or "coach" => (
                SystemPersona:
                    "You are an elite UEFA Pro License tactical analyst and data scientist. " +
                    "Your tone is technical, objective, clinical, and completely detached. " +
                    "You never use emotional rhetoric, clichés, or casual storytelling. " +
                    "You write exclusively in precise football analytics terminology (in-possession/out-of-possession structures, Zone 14, half-spaces, xG, field tilt, PPDA, pressing triggers, rest-defense). " +
                    "You output strict JSON.",
                ModeGuidelines: """
                === ACTIVE AUDIENCE PERSONA: THE ANALYST / COACH ===
                * Tone: Technical, objective, clinical, and detached. Free of emotional rhetoric, moral evaluations, or dramatic framing; focused entirely on tactical causality, space management, and execution efficiency.
                * Creativity Level: Very low (1/5). Use precise domain vocabulary and analytical structure over figurative language.
                * MUST INCLUDE:
                  - In-possession and out-of-possession structural formations (e.g., 1-3-2-4-1 build-up vs. 1-4-4-2 mid-low block, 4-3-3 vs 4-2-3-1 shifts).
                  - Advanced underlying metrics & estimates derived from the match stats: xG, xGA, passing network density, field tilt %, pass accuracy %, and PPDA (passes per defensive action).
                  - Micro-tactical mechanisms: pressing triggers, center-back pin-downs, Zone 14 occupation, half-space overloads, dynamic numerical/positional superiorities, and rest-defense structures during transitions.
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - Subjective clichés ("they lacked heart", "caught sleeping", "blistering", "electric").
                  - Play-by-play narrative commentary that does not explain tactical system mechanics.
                * REFERENCE STYLE EXAMPLE (Match this exact clinical tone and technical density!):
                  "In the 31st minute, Arsenal broke Liverpool's 1-4-2-3-1 mid-block through an asymmetry created on the right flank. The inverted winger pinned the left center-back, opening an interior passing lane into Zone 14. Odegaard received on the half-turn, drawing the double pivot out of position, and slipped a through ball into the right half-space for Saka. Following a 96-second sustained pressure sequence (71% field tilt, PPDA of 6.4), the cutback generated an estimated xG of 0.64, converted on the first touch."
                """,
                Temperature: 0.2f
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
                  - Standout performers: the fastest sprinter, the trickiest wizard on the ball, the acrobatic goalie.
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - NO boring percentages, statistics, or numbers (do not mention "58% possession" or "89% pass accuracy").
                  - NO coaching jargon ("mid-block", "tactical", "transition", "possession").
                  - NO harsh criticism or blaming players for mistakes.
                * REFERENCE STYLE EXAMPLE (Match this exact playful, metaphor-packed energy!):
                  "Out of nowhere, Bukayo Saka zoomed down the grass like a lightning bolt, zipping right past two defenders! Captain Martin Odegaard used a flash of wizard foot magic to slide a secret pass past Liverpool's giant shield wall, and Saka came flying in like a superhero to blast a rocket shot right into the net! The whole stadium exploded with giant cheers!"
                """,
                Temperature: 0.85f
            ),

            _ => (
                SystemPersona:
                    "You are an engaging, charismatic Premier League broadcast host speaking to a Casual Fan. " +
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
                  - A broad, clear overview of how the game played out (e.g., "one team dominated the ball, but the other punished them on the counter").
                * STRICTLY FORBIDDEN (DO NOT INCLUDE):
                  - Dense coaching jargon (NEVER use terms like "third-man runs", "half-spaces", "Zone 14", "PPDA", "field tilt", "mid-block", or "rest-defense").
                  - Exhaustive data sheets or raw metric dumps.
                  - Childish fairy-tale metaphors (no "wizards" or "castles").
                * REFERENCE STYLE EXAMPLE (Match this exact conversational broadcast tone!):
                  "Right around the half-hour mark, Arsenal finally broke the deadlock. After pinning Liverpool back for twenty minutes, Odegaard caught the defense flat-footed with a slick through-ball to Bukayo Saka, who buried it to make it 1-0. Just when it looked like Arsenal were cruising in the second half, Mohamed Salah silenced the stadium in the 54th minute with a clinical equalizer against the run of play—completely flipping the momentum in this massive title-race clash."
                """,
                Temperature: 0.6f
            )
        };
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
