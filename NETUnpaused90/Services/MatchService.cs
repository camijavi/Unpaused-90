using System.Collections.Concurrent;
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
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly object _simLock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, (MatchData Master, List<MatchData> Snapshots)> _matchesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _aliasToMatchId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _availableMatchIds = [];
    private string? _currentActiveMatchId;

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

        _cacheDirectory = Path.Combine(env.ContentRootPath, "obj");
        Directory.CreateDirectory(_cacheDirectory);

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
        string? matchId = null,
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

        if (!string.IsNullOrWhiteSpace(matchId))
        {
            _currentActiveMatchId = matchId;
        }

        _logger.LogInformation(
            "Reset Live Match Simulation for '{MatchId}' to starting minute {StartMinute}'.",
            matchId ?? _currentActiveMatchId ?? "default",
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
    /// Randomly picks one of the available matches in the Azure Blob container and returns its snapshot.
    /// </summary>
    public async Task<MatchData?> GetRandomMatchAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAllMatchesLoadedAsync(cancellationToken);
        if (_availableMatchIds.Count == 0)
        {
            return null;
        }

        var randomIndex = Random.Shared.Next(_availableMatchIds.Count);
        _currentActiveMatchId = _availableMatchIds[randomIndex];
        return await GetMatchAsync(_currentActiveMatchId, cancellationToken);
    }

    /// <summary>
    /// Loads the complete 90-minute real match data directly from Azure Blob Storage via Entra ID TokenCredential.
    /// </summary>
    public async Task<MatchData?> GetMasterMatchAsync(
        string? matchId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAllMatchesLoadedAsync(cancellationToken);

        string? resolvedId = null;
        if (!string.IsNullOrWhiteSpace(matchId))
        {
            if (_matchesById.ContainsKey(matchId))
            {
                resolvedId = matchId;
            }
            else if (_aliasToMatchId.TryGetValue(matchId, out var mappedId))
            {
                resolvedId = mappedId;
            }
        }

        resolvedId ??= _currentActiveMatchId ?? _availableMatchIds.FirstOrDefault();
        if (resolvedId is null || !_matchesById.TryGetValue(resolvedId, out var entry))
        {
            return null;
        }

        return entry.Master;
    }

    /// <summary>
    /// Ensures all match JSON blobs are discovered and downloaded from Azure Blob Storage using Microsoft Entra ID.
    /// </summary>
    private async Task EnsureAllMatchesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_availableMatchIds.Count > 0)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_availableMatchIds.Count > 0)
            {
                return;
            }

            var blobEndpoint = _configuration["AzureBlob:ConnectionString"]
                ?? throw new InvalidOperationException("AzureBlob:ConnectionString is missing in appsettings.json.");
            var containerName = _configuration["AzureBlob:ContainerName"] ?? "matches";
            var serviceUri = new Uri(blobEndpoint);

            _logger.LogInformation(
                "Connecting to Azure Blob Storage ({ServiceUri}) via Microsoft Entra ID to discover match datasets...",
                serviceUri);

            var blobServiceClient = new BlobServiceClient(serviceUri, _credential);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            var blobNames = new List<string>();
            try
            {
                await foreach (var blobItem in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
                {
                    if (blobItem.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        blobNames.Add(blobItem.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list blobs dynamically in container '{Container}'. Using configured fallback blob.", containerName);
            }

            if (blobNames.Count == 0)
            {
                blobNames.Add(_configuration["AzureBlob:BlobName"] ?? "arsenal-liverpool.json");
            }

            foreach (var blobName in blobNames)
            {
                try
                {
                    var blobClient = containerClient.GetBlobClient(blobName);
                    BlobDownloadResult downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
                    var json = downloadResult.Content.ToString();

                    var localCacheFile = Path.Combine(_cacheDirectory, $"cached-{blobName}");
                    try
                    {
                        await File.WriteAllTextAsync(localCacheFile, json, cancellationToken);
                    }
                    catch
                    {
                        // Ignore disk write errors
                    }

                    var (master, snapshots) = ParseMatchPayload(json);
                    _matchesById[master.MatchId] = (master, snapshots);
                    _aliasToMatchId[blobName] = master.MatchId;
                    _aliasToMatchId[Path.GetFileNameWithoutExtension(blobName)] = master.MatchId;
                    _aliasToMatchId[$"{master.HomeTeam.Name}-{master.AwayTeam.Name}"] = master.MatchId;
                    _aliasToMatchId[$"{master.HomeTeam.TeamId}-{master.AwayTeam.TeamId}"] = master.MatchId;

                    if (!_availableMatchIds.Contains(master.MatchId))
                    {
                        _availableMatchIds.Add(master.MatchId);
                    }

                    _logger.LogInformation(
                        "Loaded real match '{MatchId}' ({Home} vs {Away}) from blob '{BlobName}'.",
                        master.MatchId,
                        master.HomeTeam.Name,
                        master.AwayTeam.Name,
                        blobName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load real match data for blob '{BlobName}' from Azure Blob Storage.", blobName);
                }
            }

            if (_availableMatchIds.Count > 0 && string.IsNullOrWhiteSpace(_currentActiveMatchId))
            {
                var randomIndex = Random.Shared.Next(_availableMatchIds.Count);
                _currentActiveMatchId = _availableMatchIds[randomIndex];
                _logger.LogInformation(
                    "Selected initial demo match '{MatchId}' randomly from {Total} discovered matches.",
                    _currentActiveMatchId,
                    _availableMatchIds.Count);
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
        IReadOnlyList<MatchData>? checkpoints,
        StatPair? targetFinalPossession)
    {
        // 1. Baseline Possession from synthetic checkpoints or final target
        double homePos = 50.0;
        if (checkpoints is { Count: > 0 })
        {
            var ordered = checkpoints.OrderBy(c => c.CurrentMinute).ToList();
            if (minute <= ordered.First().CurrentMinute)
            {
                homePos = ordered.First().Statistics.Possession.Home;
            }
            else if (minute >= ordered.Last().CurrentMinute)
            {
                homePos = ordered.Last().Statistics.Possession.Home;
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
        var checkpoints = _matchesById.TryGetValue(master.MatchId, out var entry) ? entry.Snapshots : null;
        return BuildMatchSnapshotAtMinute(master, minute, checkpoints);
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
            master.Statistics.Possession);

        var dynamicHomeShots = Math.Max(homeGoals, (int)Math.Round((double)minute / 90.0 * master.Statistics.Shots.Home));
        var dynamicAwayShots = Math.Max(awayGoals, (int)Math.Round((double)minute / 90.0 * master.Statistics.Shots.Away));

        var dynamicHomeShotsOnTarget = Math.Max(homeGoals, (int)Math.Round((double)minute / 90.0 * master.Statistics.ShotsOnTarget.Home));
        var dynamicAwayShotsOnTarget = Math.Max(awayGoals, (int)Math.Round((double)minute / 90.0 * master.Statistics.ShotsOnTarget.Away));

        var dynamicHomeCorners = (int)Math.Round((double)minute / 90.0 * master.Statistics.Corners.Home);
        var dynamicAwayCorners = (int)Math.Round((double)minute / 90.0 * master.Statistics.Corners.Away);

        var dynamicHomeFouls = (int)Math.Round((double)minute / 90.0 * master.Statistics.Fouls.Home);
        var dynamicAwayFouls = (int)Math.Round((double)minute / 90.0 * master.Statistics.Fouls.Away);

        var homeYellow = activeTimeline.Count(e =>
            e.EventType.Equals("YellowCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(master.HomeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(master.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var awayYellow = activeTimeline.Count(e =>
            e.EventType.Equals("YellowCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(master.AwayTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(master.AwayTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var homeRed = activeTimeline.Count(e =>
            e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(master.HomeTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(master.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var awayRed = activeTimeline.Count(e =>
            e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals(master.AwayTeam.TeamId, StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals(master.AwayTeam.Name, StringComparison.OrdinalIgnoreCase)));

        var dynamicStats = new MatchStats(
            Possession: dynamicPossession,
            Shots: new StatPair(dynamicHomeShots, dynamicAwayShots),
            ShotsOnTarget: new StatPair(dynamicHomeShotsOnTarget, dynamicAwayShotsOnTarget),
            Corners: new StatPair(dynamicHomeCorners, dynamicAwayCorners),
            Fouls: new StatPair(dynamicHomeFouls, dynamicAwayFouls),
            PassAccuracy: master.Statistics.PassAccuracy
        );

        return master with
        {
            CurrentMinute = minute,
            Status = status,
            HomeTeam = master.HomeTeam with { Score = homeGoals },
            AwayTeam = master.AwayTeam with { Score = awayGoals },
            Statistics = dynamicStats,
            Timeline = activeTimeline,
            GeneratedInsights = activeInsights
        };
    }

    public async Task<List<MatchSummaryDto>> GetAllMatchesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAllMatchesLoadedAsync(cancellationToken);
        var currentMinute = GetCurrentSimulatedMinute();
        var list = new List<MatchSummaryDto>();

        foreach (var id in _availableMatchIds)
        {
            if (_matchesById.TryGetValue(id, out var entry))
            {
                var snapshot = BuildMatchSnapshotAtMinute(entry.Master, currentMinute);
                list.Add(new MatchSummaryDto(
                    MatchId: snapshot.MatchId,
                    Competition: snapshot.Competition,
                    Stadium: snapshot.Stadium,
                    HomeTeam: snapshot.HomeTeam.Name,
                    AwayTeam: snapshot.AwayTeam.Name,
                    HomeScore: snapshot.HomeTeam.Score,
                    AwayScore: snapshot.AwayTeam.Score,
                    Score: $"{snapshot.HomeTeam.Score}-{snapshot.AwayTeam.Score}",
                    CurrentMinute: snapshot.CurrentMinute,
                    Status: snapshot.Status
                ));
            }
        }

        return list;
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
}
