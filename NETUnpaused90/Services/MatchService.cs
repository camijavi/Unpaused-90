using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using NETUnpaused90.Models;

namespace NETUnpaused90.Services;

public class MatchService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatchService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly TokenCredential _credential;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private MatchData? _cachedMatch;

    public MatchService(
        IConfiguration configuration,
        ILogger<MatchService> logger,
        IWebHostEnvironment env)
    {
        _configuration = configuration;
        _logger = logger;
        _env = env;

        var tenantId = _configuration["AzureBlob:TenantId"];

        // Locally: authenticate directly against your Azure tenant so personal @gmail.com Microsoft accounts work.
        // In Azure App Service (Production): use Managed Identity via DefaultAzureCredential.
        _credential = _env.IsDevelopment()
            ? new ChainedTokenCredential(
                new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),
                new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenantId }))
            : new DefaultAzureCredential();
    }

    /// <summary>
    /// Fetches the match data directly from Azure Blob Storage using Microsoft Entra ID.
    /// </summary>
    public async Task<MatchData?> GetMatchAsync(string matchId, CancellationToken cancellationToken = default)
    {
        if (_cachedMatch is not null &&
            string.Equals(_cachedMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedMatch;
        }

        var matchData = await LoadMatchDataFromBlobAsync(cancellationToken);
        if (matchData is not null)
        {
            _cachedMatch = matchData;
        }

        if (matchData is not null &&
            string.Equals(matchData.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
        {
            return matchData;
        }

        return null;
    }

    public async Task<List<MatchSummaryDto>> GetAllMatchesAsync(CancellationToken cancellationToken = default)
    {
        var match = _cachedMatch ?? await LoadMatchDataFromBlobAsync(cancellationToken);
        if (match is null)
        {
            return [];
        }

        _cachedMatch = match;

        return
        [
            new MatchSummaryDto(
                MatchId: match.MatchId,
                Competition: match.Competition,
                Stadium: match.Stadium,
                HomeTeam: match.HomeTeam.Name,
                AwayTeam: match.AwayTeam.Name,
                HomeScore: match.HomeTeam.Score,
                AwayScore: match.AwayTeam.Score,
                Score: $"{match.HomeTeam.Score}-{match.AwayTeam.Score}",
                CurrentMinute: match.CurrentMinute,
                Status: match.Status
            )
        ];
    }

    public async Task<List<TimelineEvent>?> GetTimelineAsync(
        string matchId,
        int? upToMinute = null,
        CancellationToken cancellationToken = default)
    {
        var match = await GetMatchAsync(matchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var timeline = match.Timeline.AsEnumerable();
        if (upToMinute.HasValue)
        {
            timeline = timeline.Where(e => e.Minute <= upToMinute.Value);
        }

        return timeline.OrderBy(e => e.Minute).ToList();
    }

    public async Task<List<GeneratedInsight>?> GetInsightsAsync(
        string matchId,
        int? upToMinute = null,
        CancellationToken cancellationToken = default)
    {
        var match = await GetMatchAsync(matchId, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var insights = match.GeneratedInsights.AsEnumerable();
        if (upToMinute.HasValue)
        {
            insights = insights.Where(i => i.Minute <= upToMinute.Value);
        }

        return insights.OrderBy(i => i.Minute).ToList();
    }

    private async Task<MatchData?> LoadMatchDataFromBlobAsync(CancellationToken cancellationToken)
    {
        var blobEndpoint = _configuration["AzureBlob:ConnectionString"]
            ?? throw new InvalidOperationException("AzureBlob:ConnectionString is missing in appsettings.json.");
        var containerName = _configuration["AzureBlob:ContainerName"] ?? "matches";
        var blobName = _configuration["AzureBlob:BlobName"] ?? "arsenal-liverpool.json";

        var serviceUri = new Uri(blobEndpoint);

        _logger.LogInformation(
            "Connecting to Azure Blob Storage ({ServiceUri}) via Microsoft Entra ID...",
            serviceUri);

        var blobServiceClient = new BlobServiceClient(serviceUri, _credential);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        BlobDownloadResult downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
        var json = downloadResult.Content.ToString();

        _logger.LogInformation(
            "Successfully downloaded '{BlobName}' from Azure Blob container '{ContainerName}'.",
            blobName,
            containerName);

        return JsonSerializer.Deserialize<MatchData>(json, _jsonOptions);
    }
}
