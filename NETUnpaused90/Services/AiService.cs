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

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write all JSON field values in Spanish."
            : "Write all JSON field values in English.";

        var prompt = $@"
You are a Premier League tactical analyst for Unpaused 90'.
A fan stopped watching at minute {request.LastViewedMinute} and joined back at minute {request.CurrentMinute}.

MATCH DATA SLICE:
- Match: {match.HomeTeam.Name} vs {match.AwayTeam.Name} (Score at minute {request.CurrentMinute}: {homeGoalsAtCurrent}-{awayGoalsAtCurrent})
- Overall Match Statistics: {JsonSerializer.Serialize(match.Statistics)}
- Events Occurred Between Minute {request.LastViewedMinute}' and {request.CurrentMinute}': {JsonSerializer.Serialize(filteredEvents)}
- Key Moment Insights in Window: {JsonSerializer.Serialize(filteredInsights)}

AUDIENCE MODE: {request.ViewMode}
- If 'analyst': Include passing accuracy, possession numbers, high-pressing duration, and tactical structure.
- If 'casual': Use engaging, clear football storytelling focused on the big moments and flow of the match.
- If 'kid': Use fun, energetic metaphors (like castles, rockets, and superheroes) and simple vocabulary.

FAVORITE CLUB BIAS: {request.FavoriteTeam ?? "Neutral"}
- Frame the narrative, concerns, and excitement from the perspective of a {request.FavoriteTeam ?? "Neutral"} supporter while staying factual.

LANGUAGE: {languageInstruction}

TASK:
1. whatHappened: Explain WHAT happened between minute {request.LastViewedMinute}' and {request.CurrentMinute}' (factual progression of play).
2. whyItMatters: Explain WHY IT MATTERS (tactical context, passing sequences, sustained pressure, why goals/events occurred).
3. momentumChanges: Describe how momentum shifted during this window and which side currently controls the tempo.
4. whatToWatchNext: Highlight WHAT TO WATCH NEXT after minute {request.CurrentMinute}' (spaces opening up, key player matchups, tactical adjustments).

OUTPUT FORMAT:
Return ONLY a valid JSON object with the exact keys:
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
            Temperature = 0.6f
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage("You are an expert Premier League match intelligence AI that outputs strict JSON."),
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

        var lengthInstruction = request.Length?.ToLowerInvariant() switch
        {
            "30s" => "Write a concise 30-second recap (1 short paragraph, ~60 words).",
            "deep-dive" or "deepdive" => "Write a comprehensive tactical deep dive (3 paragraphs covering buildup, turning points, and statistical dominance).",
            _ => "Write a standard 1-minute summary (5 clear bullet points or a 2-paragraph story)."
        };

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write the story in Spanish."
            : "Write the story in English.";

        var prompt = $@"
Generate a personalized Premier League match recap for {match.HomeTeam.Name} ({match.HomeTeam.Score}) vs {match.AwayTeam.Name} ({match.AwayTeam.Score}).

MATCH DATA:
- Statistics: {JsonSerializer.Serialize(match.Statistics)}
- Timeline Events: {JsonSerializer.Serialize(match.Timeline)}
- Key Insights: {JsonSerializer.Serialize(match.GeneratedInsights)}

AUDIENCE MODE: {request.Mode} (analyst, casual, or kid)
FAVORITE TEAM PERSPECTIVE: {request.FavoriteTeam ?? "Neutral"}
LENGTH: {lengthInstruction}
LANGUAGE: {languageInstruction}

Return ONLY a valid JSON object with the exact key:
{{
  ""story"": ""...""
}}";

        var chatClient = GetChatClient();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = 0.6f
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage("You are an expert Premier League match intelligence AI that outputs strict JSON."),
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

        var languageInstruction = string.Equals(request.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? "Write title and explanation in Spanish."
            : "Write title and explanation in English.";

        var prompt = $@"
Explain WHY this football event matters in the context of the match ({match.HomeTeam.Name} vs {match.AwayTeam.Name}), rather than just reporting that it happened.

TARGET EVENT:
{JsonSerializer.Serialize(targetEvent)}

PRECEDING MATCH EVENTS FOR CONTEXT:
{JsonSerializer.Serialize(precedingEvents)}

OVERALL STATS:
{JsonSerializer.Serialize(match.Statistics)}

AUDIENCE MODE: {request.ViewMode} (analyst, casual, or kid)
LANGUAGE: {languageInstruction}

Return ONLY a valid JSON object with the exact keys:
{{
  ""title"": ""Short headline explaining the significance"",
  ""explanation"": ""2-3 sentences explaining the tactical cause and impact on momentum.""
}}";

        var chatClient = GetChatClient();
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            Temperature = 0.5f
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage("You are an expert Premier League match intelligence AI that outputs strict JSON."),
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

    private ChatClient GetChatClient()
    {
        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var apiKey = _configuration["AzureOpenAI:ApiKey"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.StartsWith("<"))
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint is not configured in appsettings.json. Set it to your Azure OpenAI / Microsoft Foundry endpoint URL.");
        }

        var endpointUri = new Uri(endpoint);
        AzureOpenAIClient azureClient;

        // If an API key is provided, use it; otherwise authenticate via Microsoft Entra ID (safe for public repos!)
        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("<"))
        {
            _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) using API Key...", endpointUri);
            azureClient = new AzureOpenAIClient(endpointUri, new ApiKeyCredential(apiKey));
        }
        else
        {
            _logger.LogInformation("Connecting to Azure OpenAI ({Endpoint}) via Microsoft Entra ID...", endpointUri);
            azureClient = new AzureOpenAIClient(endpointUri, _credential);
        }

        return azureClient.GetChatClient(deploymentName);
    }
}
