using System.Text.Json;
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
    }

    /// <summary>
    /// Fetches the match data from Azure Blob Storage using Microsoft Entra ID (DefaultAzureCredential).
    /// Falls back to the local arsenal-liverpool.json file if Azure Blob Storage is unreachable locally.
    /// </summary>
    public async Task<MatchData?> GetMatchAsync(string matchId, CancellationToken cancellationToken = default)
    {
        if (_cachedMatch is not null &&
            string.Equals(_cachedMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedMatch;
        }

        var matchData = await LoadMatchDataAsync(cancellationToken);
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
        var match = _cachedMatch ?? await LoadMatchDataAsync(cancellationToken);
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

    private async Task<MatchData?> LoadMatchDataAsync(CancellationToken cancellationToken)
    {
        var blobEndpointOrConnString = _configuration["AzureBlob:ConnectionString"];
        var containerName = _configuration["AzureBlob:ContainerName"] ?? "match";
        var blobName = _configuration["AzureBlob:BlobName"] ?? "arsenal-liverpool.json";

        if (!string.IsNullOrWhiteSpace(blobEndpointOrConnString) &&
            !blobEndpointOrConnString.StartsWith("<"))
        {
            try
            {
                BlobServiceClient blobServiceClient;

                // Authenticate with Microsoft Entra ID (DefaultAzureCredential) when given an HTTPS endpoint URL
                if (Uri.TryCreate(blobEndpointOrConnString, UriKind.Absolute, out var serviceUri) &&
                    (serviceUri.Scheme == Uri.UriSchemeHttps || serviceUri.Scheme == Uri.UriSchemeHttp))
                {
                    _logger.LogInformation(
                        "Connecting to Azure Blob Storage ({ServiceUri}) via Microsoft Entra ID (DefaultAzureCredential)...",
                        serviceUri);

                    blobServiceClient = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
                }
                else
                {
                    blobServiceClient = new BlobServiceClient(blobEndpointOrConnString);
                }

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                BlobDownloadResult downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
                var json = downloadResult.Content.ToString();

                _logger.LogInformation(
                    "Successfully loaded '{BlobName}' from Azure Blob container '{ContainerName}'.",
                    blobName,
                    containerName);

                return JsonSerializer.Deserialize<MatchData>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Azure Blob Storage access failed ({Message}). Falling back to local file '{BlobName}'.",
                    ex.Message,
                    blobName);
            }
        }

        // Fallback to local arsenal-liverpool.json in repository root or content root
        var candidatePaths = new[]
        {
            Path.Combine(_env.ContentRootPath, blobName),
            Path.Combine(_env.ContentRootPath, "..", blobName)
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogInformation("Loading match data from local file: {FilePath}", Path.GetFullPath(path));
                var localJson = await File.ReadAllTextAsync(path, cancellationToken);
                return JsonSerializer.Deserialize<MatchData>(localJson, _jsonOptions);
            }
        }

        _logger.LogError("Could not find match data in Azure Blob Storage or local path.");
        return null;
    }
}
