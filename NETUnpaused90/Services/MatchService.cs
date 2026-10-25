using System.Text.Json;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using NETUnpaused90.Models;

namespace NETUnpaused90.Services;

public class MatchService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatchService> _logger;
    private readonly TokenCredential _credential;
    private readonly string _localCacheFilePath;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private MatchData? _cachedMatch;

    public MatchService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<MatchService> logger,
        TokenCredential credential)
    {
        _configuration = configuration;
        _logger = logger;
        _credential = credential;

        var cacheDir = Path.Combine(env.ContentRootPath, "obj");
        Directory.CreateDirectory(cacheDir);
        _localCacheFilePath = Path.Combine(cacheDir, "cached-arsenal-liverpool.json");
    }

    /// <summary>
    /// Fetches the match data from Azure Blob Storage (with thread-safe caching and instant fallback so startup requests never hang).
    /// </summary>
    public async Task<MatchData?> GetMatchAsync(string matchId, CancellationToken cancellationToken = default)
    {
        if (_cachedMatch is not null &&
            string.Equals(_cachedMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedMatch;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedMatch is not null &&
                string.Equals(_cachedMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedMatch;
            }

            // Try local disk cache first if available
            if (File.Exists(_localCacheFilePath))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(_localCacheFilePath, cancellationToken);
                    var diskMatch = JsonSerializer.Deserialize<MatchData>(cachedJson, _jsonOptions);
                    if (diskMatch is not null)
                    {
                        _cachedMatch = diskMatch;
                        // Trigger non-blocking background refresh from Azure Blob Storage
                        _ = Task.Run(() => RefreshFromBlobStorageSilentlyAsync());
                        return _cachedMatch;
                    }
                }
                catch
                {
                    // Continue to live blob fetch
                }
            }

            // Try live Azure Blob Storage with a 5-second timeout before using fallback
            try
            {
                using var blobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                blobCts.CancelAfter(TimeSpan.FromSeconds(5));
                var matchData = await LoadMatchDataFromBlobAsync(blobCts.Token);
                if (matchData is not null)
                {
                    _cachedMatch = matchData;
                    return _cachedMatch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Azure Blob Storage initial fetch timed out or failed ({Message}); using built-in PL2027-001 dataset and retrying in background.",
                    ex.Message);
                _ = Task.Run(() => RefreshFromBlobStorageSilentlyAsync());
            }

            _cachedMatch = GetBuiltInFallbackMatch();
            return _cachedMatch;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task RefreshFromBlobStorageSilentlyAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var fresh = await LoadMatchDataFromBlobAsync(cts.Token);
            if (fresh is not null)
            {
                _cachedMatch = fresh;
            }
        }
        catch
        {
            // Keep existing cached match data
        }
    }

    public async Task<List<MatchSummaryDto>> GetAllMatchesAsync(CancellationToken cancellationToken = default)
    {
        var match = await GetMatchAsync("PL2027-001", cancellationToken);
        if (match is null)
        {
            return [];
        }

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

        try
        {
            await File.WriteAllTextAsync(_localCacheFilePath, json, cancellationToken);
        }
        catch
        {
            // Ignore local cache write errors
        }

        _logger.LogInformation(
            "Successfully downloaded '{BlobName}' from Azure Blob container '{ContainerName}'.",
            blobName,
            containerName);

        return JsonSerializer.Deserialize<MatchData>(json, _jsonOptions);
    }

    private MatchData GetBuiltInFallbackMatch()
    {
        const string fallbackJson = """
        {
          "matchId": "PL2027-001",
          "competition": "Premier League",
          "season": "2026/27",
          "stadium": "Emirates Stadium",
          "status": "FullTime",
          "currentMinute": 90,
          "homeTeam": {
            "teamId": "ARS",
            "name": "Arsenal",
            "coach": "Mikel Arteta",
            "formation": "4-3-3",
            "score": 2,
            "startingXI": [
              { "playerId": "ARS01", "name": "David Raya", "number": 1, "position": "GK", "captain": false },
              { "playerId": "ARS02", "name": "Ben White", "number": 4, "position": "RB", "captain": false },
              { "playerId": "ARS03", "name": "William Saliba", "number": 2, "position": "CB", "captain": false },
              { "playerId": "ARS04", "name": "Gabriel Magalhaes", "number": 6, "position": "CB", "captain": false },
              { "playerId": "ARS05", "name": "Oleksandr Zinchenko", "number": 35, "position": "LB", "captain": false },
              { "playerId": "ARS06", "name": "Declan Rice", "number": 41, "position": "CM", "captain": false },
              { "playerId": "ARS07", "name": "Martin Odegaard", "number": 8, "position": "CM", "captain": true },
              { "playerId": "ARS08", "name": "Kai Havertz", "number": 29, "position": "CM", "captain": false },
              { "playerId": "ARS09", "name": "Bukayo Saka", "number": 7, "position": "RW", "captain": false },
              { "playerId": "ARS10", "name": "Gabriel Jesus", "number": 9, "position": "ST", "captain": false },
              { "playerId": "ARS11", "name": "Gabriel Martinelli", "number": 11, "position": "LW", "captain": false }
            ],
            "substitutes": [
              { "playerId": "ARS12", "name": "Aaron Ramsdale", "number": 32, "position": "GK" },
              { "playerId": "ARS13", "name": "Jakub Kiwior", "number": 15, "position": "CB" },
              { "playerId": "ARS14", "name": "Jorginho", "number": 20, "position": "CM" },
              { "playerId": "ARS15", "name": "Leandro Trossard", "number": 19, "position": "LW" },
              { "playerId": "ARS16", "name": "Eddie Nketiah", "number": 14, "position": "ST" }
            ]
          },
          "awayTeam": {
            "teamId": "LIV",
            "name": "Liverpool",
            "coach": "Arne Slot",
            "formation": "4-2-3-1",
            "score": 1,
            "startingXI": [
              { "playerId": "LIV01", "name": "Alisson Becker", "number": 1, "position": "GK", "captain": false },
              { "playerId": "LIV02", "name": "Trent Alexander-Arnold", "number": 66, "position": "RB", "captain": false },
              { "playerId": "LIV03", "name": "Ibrahima Konate", "number": 5, "position": "CB", "captain": false },
              { "playerId": "LIV04", "name": "Virgil van Dijk", "number": 4, "position": "CB", "captain": true },
              { "playerId": "LIV05", "name": "Andrew Robertson", "number": 26, "position": "LB", "captain": false },
              { "playerId": "LIV06", "name": "Alexis Mac Allister", "number": 10, "position": "CM", "captain": false },
              { "playerId": "LIV07", "name": "Dominik Szoboszlai", "number": 8, "position": "CM", "captain": false },
              { "playerId": "LIV08", "name": "Mohamed Salah", "number": 11, "position": "RW", "captain": false },
              { "playerId": "LIV09", "name": "Curtis Jones", "number": 17, "position": "AM", "captain": false },
              { "playerId": "LIV10", "name": "Luis Diaz", "number": 7, "position": "LW", "captain": false },
              { "playerId": "LIV11", "name": "Darwin Nunez", "number": 9, "position": "ST", "captain": false }
            ],
            "substitutes": [
              { "playerId": "LIV12", "name": "Caoimhin Kelleher", "number": 62, "position": "GK" },
              { "playerId": "LIV13", "name": "Joe Gomez", "number": 2, "position": "CB" },
              { "playerId": "LIV14", "name": "Wataru Endo", "number": 3, "position": "CM" },
              { "playerId": "LIV15", "name": "Cody Gakpo", "number": 18, "position": "FW" },
              { "playerId": "LIV16", "name": "Harvey Elliott", "number": 19, "position": "AM" }
            ]
          },
          "statistics": {
            "possession": { "home": 54, "away": 46 },
            "shots": { "home": 14, "away": 11 },
            "shotsOnTarget": { "home": 6, "away": 5 },
            "passAccuracy": { "home": 87, "away": 84 },
            "corners": { "home": 7, "away": 5 },
            "fouls": { "home": 9, "away": 11 }
          },
          "timeline": [
            { "eventId": "EV001", "minute": 12, "eventType": "Shot", "team": "ARS", "player": "Kai Havertz", "onTarget": true },
            { "eventId": "EV002", "minute": 24, "eventType": "PressureSequence", "team": "ARS", "durationSeconds": 96, "importanceScore": 75 },
            { "eventId": "EV003", "minute": 31, "eventType": "Goal", "team": "ARS", "player": "Bukayo Saka", "assist": "Martin Odegaard", "importanceScore": 100 },
            { "eventId": "EV004", "minute": 43, "eventType": "YellowCard", "team": "LIV", "player": "Alexis Mac Allister" },
            { "eventId": "EV005", "minute": 54, "eventType": "Goal", "team": "LIV", "player": "Mohamed Salah", "assist": "Dominik Szoboszlai", "importanceScore": 95 },
            { "eventId": "EV006", "minute": 66, "eventType": "Substitution", "team": "ARS", "playerOut": "Gabriel Martinelli", "playerIn": "Leandro Trossard" },
            { "eventId": "EV007", "minute": 72, "eventType": "PressureSequence", "team": "ARS", "durationSeconds": 114, "importanceScore": 88 },
            { "eventId": "EV008", "minute": 79, "eventType": "Goal", "team": "ARS", "player": "Kai Havertz", "assist": "Bukayo Saka", "importanceScore": 100 }
          ],
          "generatedInsights": [
            {
              "minute": 31,
              "type": "GoalImpact",
              "summary": "Arsenal opened the scoring after a sustained spell of pressure in Liverpool's defensive third.",
              "reason": "Odegaard found space between the lines and assisted Saka inside the box."
            },
            {
              "minute": 54,
              "type": "MomentumShift",
              "summary": "Liverpool equalized through a fast transition led by Szoboszlai and Salah.",
              "reason": "Arsenal's high defensive line left space behind Zinchenko."
            },
            {
              "minute": 79,
              "type": "WinningMoment",
              "summary": "Arsenal regained the lead late in the second half.",
              "reason": "Sustained wing pressure created a second-ball opportunity finished by Havertz."
            }
          ]
        }
        """;
        return JsonSerializer.Deserialize<MatchData>(fallbackJson, _jsonOptions)!;
    }
}
