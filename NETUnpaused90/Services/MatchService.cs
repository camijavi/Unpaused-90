using System.Text.Json;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using NETUnpaused90.Models;

namespace NETUnpaused90.Services;

public class MatchService
{
    public const double DefaultSecondsPerSimulatedMinute = 30.0;

    private readonly double _secondsPerSimulatedMinute;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatchService> _logger;
    private readonly TokenCredential _credential;
    private readonly string _localCacheFilePath;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly object _simLock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private MatchData? _masterMatch;
    private List<MatchData> _syntheticSnapshots = [];
    private int _simulationStartMinute;
    private DateTimeOffset _simulationStartUtc;

    public double SecondsPerSimulatedMinute => _secondsPerSimulatedMinute;

    public MatchService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<MatchService> logger,
        TokenCredential credential)
    {
        _configuration = configuration;
        _logger = logger;
        _credential = credential;

        _secondsPerSimulatedMinute = configuration.GetValue<double?>("Simulation:SecondsPerMinute")
            ?? DefaultSecondsPerSimulatedMinute;

        var cacheDir = Path.Combine(env.ContentRootPath, "obj");
        Directory.CreateDirectory(cacheDir);
        _localCacheFilePath = Path.Combine(cacheDir, "cached-match-data.json");

        // Simulation starts at a random minute between 1 and 90 to simulate a match in progress
        _simulationStartMinute = Random.Shared.Next(1, 91);
        _simulationStartUtc = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Initialized Live Match Simulation starting at random minute {StartMinute}' (advances 1 min every {Sec}s).",
            _simulationStartMinute,
            _secondsPerSimulatedMinute);
    }

    /// <summary>
    /// Computes the current simulated match minute (1..90) based on elapsed time since simulation start.
    /// </summary>
    public int GetCurrentSimulatedMinute()
    {
        lock (_simLock)
        {
            var elapsedSeconds = (DateTimeOffset.UtcNow - _simulationStartUtc).TotalSeconds;
            var minutesAdvanced = (int)Math.Floor(elapsedSeconds / _secondsPerSimulatedMinute);
            return Math.Clamp(_simulationStartMinute + minutesAdvanced, 1, 90);
        }
    }

    /// <summary>
    /// Resets the live simulation to a new random minute between 1 and 90 (or an explicit minute if provided).
    /// </summary>
    public async Task<MatchData?> ResetSimulationAsync(
        string matchId,
        int? explicitStartMinute = null,
        CancellationToken cancellationToken = default)
    {
        lock (_simLock)
        {
            _simulationStartMinute = explicitStartMinute.HasValue
                ? Math.Clamp(explicitStartMinute.Value, 1, 90)
                : Random.Shared.Next(1, 91);
            _simulationStartUtc = DateTimeOffset.UtcNow;
        }

        _logger.LogInformation(
            "Reset Live Match Simulation for '{MatchId}' to starting minute {StartMinute}'.",
            matchId,
            _simulationStartMinute);

        return await GetMatchAsync(matchId, cancellationToken);
    }

    /// <summary>
    /// Returns the time-sliced MatchData snapshot for the current simulated match minute.
    /// Derived dynamically from the real match dataset loaded from Azure Blob Storage.
    /// </summary>
    public async Task<MatchData?> GetMatchAsync(
        string? matchId = null,
        CancellationToken cancellationToken = default,
        int? overrideMinute = null)
    {
        var master = await GetMasterMatchAsync(matchId, cancellationToken);
        if (master is null)
        {
            return null;
        }

        var currentMinute = overrideMinute.HasValue
            ? Math.Clamp(overrideMinute.Value, 1, 90)
            : GetCurrentSimulatedMinute();

        return BuildMatchSnapshotAtMinute(master, currentMinute);
    }

    /// <summary>
    /// Loads the complete 90-minute real match data directly from Azure Blob Storage via Entra ID TokenCredential.
    /// </summary>
    public async Task<MatchData?> GetMasterMatchAsync(
        string? matchId = null,
        CancellationToken cancellationToken = default)
    {
        if (_masterMatch is not null &&
            (string.IsNullOrWhiteSpace(matchId) || string.Equals(_masterMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase)))
        {
            return _masterMatch;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_masterMatch is not null &&
                (string.IsNullOrWhiteSpace(matchId) || string.Equals(_masterMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase)))
            {
                return _masterMatch;
            }

            try
            {
                _masterMatch = await LoadMatchDataFromBlobAsync(cancellationToken);
                return _masterMatch;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load real match data directly from Azure Blob Storage.");

                if (File.Exists(_localCacheFilePath))
                {
                    _logger.LogWarning("Falling back to local disk copy of downloaded match data: {Path}", _localCacheFilePath);
                    var cachedJson = await File.ReadAllTextAsync(_localCacheFilePath, cancellationToken);
                    var (master, snapshots) = ParseMatchPayload(cachedJson);
                    _masterMatch = master;
                    _syntheticSnapshots = snapshots;
                    return _masterMatch;
                }

                throw;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Parses a JSON string that may be either an array of 5-minute MatchData checkpoints or a single MatchData object.
    /// </summary>
    private (MatchData Master, List<MatchData> Snapshots) ParseMatchPayload(string json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<MatchData>>(json, _jsonOptions);
            if (list is { Count: > 0 })
            {
                var ordered = list.OrderBy(m => m.CurrentMinute).ToList();
                var master = ordered.Last();
                return (master, ordered);
            }
        }
        catch
        {
            // Fall through to single object
        }

        var single = JsonSerializer.Deserialize<MatchData>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize MatchData payload from Azure Blob Storage.");
        return (single, [single]);
    }

    /// <summary>
    /// Manages and calculates dynamic possession percentages for the current simulated minute based on real match teams and events.
    /// </summary>
    public static StatPair ManagePossessionUpdates(
        int minute,
        IReadOnlyList<TimelineEvent> activeEvents,
        Team homeTeam,
        Team awayTeam,
        IReadOnlyList<MatchData>? checkpoints = null,
        StatPair? targetFinalPossession = null)
    {
        double homePos;

        // 1. If interval checkpoints from blob are available, interpolate smoothly between them
        if (checkpoints is { Count: > 0 })
        {
            var ordered = checkpoints.OrderBy(c => c.CurrentMinute).ToList();
            if (minute <= ordered[0].CurrentMinute)
            {
                var first = ordered[0];
                var kickoffHome = targetFinalPossession?.Home ?? 50.0;
                var t = (double)minute / Math.Max(1, first.CurrentMinute);
                homePos = kickoffHome + t * (first.Statistics.Possession.Home - kickoffHome);
            }
            else if (minute >= ordered[^1].CurrentMinute)
            {
                homePos = ordered[^1].Statistics.Possession.Home;
            }
            else
            {
                var c1 = ordered.Last(c => c.CurrentMinute <= minute);
                var c2 = ordered.First(c => c.CurrentMinute > minute);
                var span = Math.Max(1, c2.CurrentMinute - c1.CurrentMinute);
                var t = (double)(minute - c1.CurrentMinute) / span;
                homePos = c1.Statistics.Possession.Home + t * (c2.Statistics.Possession.Home - c1.Statistics.Possession.Home);
            }
        }
        else
        {
            var baselineHome = targetFinalPossession?.Home ?? 50.0;
            homePos = baselineHome;
        }

        // 2. Dynamic Event-Driven Micro-Adjustments (Pressure Sequences, Red Cards) using real match team identifiers
        double eventDelta = 0.0;

        var homeRedCards = activeEvents.Count(e =>
            e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(homeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(homeTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var awayRedCards = activeEvents.Count(e =>
            e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(awayTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(awayTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var netManAdvantage = awayRedCards - homeRedCards;
        eventDelta += netManAdvantage * 2.5;

        // Recent Pressure Sequences (within the last 3 match minutes)
        var recentPressure = activeEvents
            .Where(e => e.Minute >= minute - 3 && e.Minute <= minute &&
                        e.EventType.Equals("PressureSequence", StringComparison.OrdinalIgnoreCase));

        foreach (var seq in recentPressure)
        {
            var isHome = seq.Team.Equals(homeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
                         seq.Team.Equals(homeTeam.Name, StringComparison.OrdinalIgnoreCase);
            var durationFactor = Math.Clamp((seq.DurationSeconds ?? 60) / 60.0, 0.5, 2.0);
            eventDelta += (isHome ? 1.5 : -1.5) * durationFactor;
        }

        // 3. Final Clamping and Normalization (Ensuring Home + Away == 100)
        var finalHome = (int)Math.Round(homePos + eventDelta);
        finalHome = Math.Clamp(finalHome, 20, 80);
        var finalAway = 100 - finalHome;

        return new StatPair(finalHome, finalAway);
    }

    /// <summary>
    /// Projects the 90-minute master match data onto the specified minute M (1..90).
    /// </summary>
    public MatchData BuildMatchSnapshotAtMinute(MatchData master, int minute)
    {
        return BuildMatchSnapshotAtMinute(master, minute, _syntheticSnapshots);
    }

    /// <summary>
    /// Static projection of 90-minute master match data onto the specified minute M (1..90).
    /// </summary>
    public static MatchData BuildMatchSnapshotAtMinute(
        MatchData master,
        int minute,
        IReadOnlyList<MatchData>? checkpoints)
    {
        var activeTimeline = master.Timeline
            .Where(e => e.Minute <= minute)
            .OrderBy(e => e.Minute)
            .ToList();

        var activeInsights = master.GeneratedInsights
            .Where(i => i.Minute <= minute)
            .OrderBy(i => i.Minute)
            .ToList();

        var homeGoals = activeTimeline.Count(e =>
            e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(master.HomeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(master.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var awayGoals = activeTimeline.Count(e =>
            e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(master.AwayTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(master.AwayTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var status = minute switch
        {
            < 45 => "1st Half",
            45 => "Half Time",
            < 90 => "2nd Half",
            _ => "FullTime"
        };

        // Manage dynamic possession updates as the live match progresses and events occur
        var dynamicPossession = ManagePossessionUpdates(
            minute,
            activeTimeline,
            master.HomeTeam,
            master.AwayTeam,
            checkpoints,
            master.Statistics?.Possession);

        // Scale cumulative statistics proportionally to the elapsed match minute, reaching the exact final stats at 90'
        MatchStats statsAtMinute;
        if (minute >= 90)
        {
            statsAtMinute = (master.Statistics ?? new MatchStats(
                Possession: dynamicPossession,
                Shots: new StatPair(0, 0),
                ShotsOnTarget: new StatPair(0, 0),
                PassAccuracy: new StatPair(0, 0),
                Corners: new StatPair(0, 0),
                Fouls: new StatPair(0, 0)
            )) with { Possession = dynamicPossession };
        }
        else
        {
            var ratio = Math.Clamp(minute / 90.0, 0.05, 1.0);
            var homeShotsOnTargetEvents = activeTimeline.Count(e =>
                (e.Team.Equals(master.HomeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
                 e.Team.Equals(master.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)) &&
                (e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) ||
                 (e.EventType.Equals("Shot", StringComparison.OrdinalIgnoreCase) && e.OnTarget == true)));

            var awayShotsOnTargetEvents = activeTimeline.Count(e =>
                (e.Team.Equals(master.AwayTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
                 e.Team.Equals(master.AwayTeam.Name, StringComparison.OrdinalIgnoreCase)) &&
                (e.EventType.Equals("Goal", StringComparison.OrdinalIgnoreCase) ||
                 (e.EventType.Equals("Shot", StringComparison.OrdinalIgnoreCase) && e.OnTarget == true)));

            var finalHomeSOT = master.Statistics?.ShotsOnTarget?.Home ?? 0;
            var finalAwaySOT = master.Statistics?.ShotsOnTarget?.Away ?? 0;
            var finalHomeShots = master.Statistics?.Shots?.Home ?? 0;
            var finalAwayShots = master.Statistics?.Shots?.Away ?? 0;
            var finalHomeCorners = master.Statistics?.Corners?.Home ?? 0;
            var finalAwayCorners = master.Statistics?.Corners?.Away ?? 0;
            var finalHomeFouls = master.Statistics?.Fouls?.Home ?? 0;
            var finalAwayFouls = master.Statistics?.Fouls?.Away ?? 0;
            var passAccuracy = master.Statistics?.PassAccuracy ?? new StatPair(85, 82);

            var homeSOT = Math.Max(homeShotsOnTargetEvents, (int)Math.Round(finalHomeSOT * ratio));
            var awaySOT = Math.Max(awayShotsOnTargetEvents, (int)Math.Round(finalAwaySOT * ratio));
            var homeShots = Math.Max(homeSOT, (int)Math.Round(finalHomeShots * ratio));
            var awayShots = Math.Max(awaySOT, (int)Math.Round(finalAwayShots * ratio));

            statsAtMinute = new MatchStats(
                Possession: dynamicPossession,
                Shots: new StatPair(homeShots, awayShots),
                ShotsOnTarget: new StatPair(homeSOT, awaySOT),
                PassAccuracy: passAccuracy,
                Corners: new StatPair(
                    (int)Math.Round(finalHomeCorners * ratio),
                    (int)Math.Round(finalAwayCorners * ratio)),
                Fouls: new StatPair(
                    (int)Math.Round(finalHomeFouls * ratio),
                    (int)Math.Round(finalAwayFouls * ratio))
            );
        }

        return master with
        {
            CurrentMinute = minute,
            Status = status,
            HomeTeam = master.HomeTeam with { Score = homeGoals },
            AwayTeam = master.AwayTeam with { Score = awayGoals },
            Statistics = statsAtMinute,
            Timeline = activeTimeline,
            GeneratedInsights = activeInsights
        };
    }

    public async Task RefreshFromBlobStorageSilentlyAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var fresh = await LoadMatchDataFromBlobAsync(cts.Token);
            if (fresh is not null)
            {
                _masterMatch = fresh;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Background refresh from Azure Blob Storage skipped ({Message}).", ex.Message);
        }
    }

    public async Task<List<MatchSummaryDto>> GetAllMatchesAsync(CancellationToken cancellationToken = default)
    {
        var master = await GetMasterMatchAsync(cancellationToken: cancellationToken);
        if (master is null)
        {
            return [];
        }

        var match = await GetMatchAsync(master.MatchId, cancellationToken);
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
        var effectiveMinute = upToMinute ?? GetCurrentSimulatedMinute();
        var match = await GetMatchAsync(matchId, cancellationToken, effectiveMinute);
        return match?.Timeline;
    }

    public async Task<List<GeneratedInsight>?> GetInsightsAsync(
        string matchId,
        int? upToMinute = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveMinute = upToMinute ?? GetCurrentSimulatedMinute();
        var match = await GetMatchAsync(matchId, cancellationToken, effectiveMinute);
        return match?.GeneratedInsights;
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
            // Ignore disk cache write errors
        }

        _logger.LogInformation(
            "Successfully downloaded '{BlobName}' from Azure Blob container '{ContainerName}'.",
            blobName,
            containerName);

        var (master, snapshots) = ParseMatchPayload(json);
        _syntheticSnapshots = snapshots;
        return master;
    }
}
