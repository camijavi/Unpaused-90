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
        _localCacheFilePath = Path.Combine(cacheDir, "cached-arsenal-liverpool.json");

        // Step 2: Start at a random minute between 1 and 90 to simulate a match already in progress
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
    /// Strictly follows the predefined synthetic match data (no random events generated).
    /// </summary>
    public async Task<MatchData?> GetMatchAsync(
        string matchId,
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
    /// Loads the complete 90-minute synthetic match blueprint from Azure Blob Storage (with local cache fallback).
    /// </summary>
    public async Task<MatchData?> GetMasterMatchAsync(
        string matchId,
        CancellationToken cancellationToken = default)
    {
        if (_masterMatch is not null &&
            string.Equals(_masterMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
        {
            return _masterMatch;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_masterMatch is not null &&
                string.Equals(_masterMatch.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
            {
                return _masterMatch;
            }

            if (File.Exists(_localCacheFilePath))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(_localCacheFilePath, cancellationToken);
                    var (master, snapshots) = ParseMatchPayload(cachedJson);
                    if (master is not null)
                    {
                        _masterMatch = master;
                        _syntheticSnapshots = snapshots;
                        _logger.LogInformation(
                            "Loaded match '{MatchId}' from local cache with {Count} interval checkpoints and {Events} master events.",
                            master.MatchId,
                            snapshots.Count,
                            master.Timeline.Count);
                        _ = Task.Run(() => RefreshFromBlobStorageSilentlyAsync());
                        return _masterMatch;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to parse cached match file ({Message}); falling back to live blob fetch.", ex.Message);
                }
            }

            try
            {
                using var blobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                blobCts.CancelAfter(TimeSpan.FromSeconds(5));
                var matchData = await LoadMatchDataFromBlobAsync(blobCts.Token);
                if (matchData is not null)
                {
                    _masterMatch = matchData;
                    return _masterMatch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Azure Blob Storage initial fetch timed out or failed ({Message}); using built-in PL2027-001 dataset and retrying in background.",
                    ex.Message);
                _ = Task.Run(() => RefreshFromBlobStorageSilentlyAsync());
            }

            _masterMatch = GetBuiltInFallbackMatch();
            return _masterMatch;
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
            ?? throw new InvalidOperationException("Failed to deserialize MatchData payload.");
        return (single, [single]);
    }

    /// <summary>
    /// Manages and calculates dynamic possession percentages for the current simulated minute.
    /// As the live match progresses and key events occur (pressure sequences, red cards, goals),
    /// this function continuously computes the updated possession balance between home and away teams.
    /// </summary>
    /// <param name="minute">The current match minute (1..90).</param>
    /// <param name="activeEvents">All timeline events that have occurred up to current minute.</param>
    /// <param name="checkpoints">Predefined synthetic match checkpoints at 5-minute intervals, if available.</param>
    /// <param name="targetFinalPossession">Baseline possession target (e.g. 60-40 or 54-46).</param>
    /// <returns>A StatPair where Home and Away possession percentages strictly sum to 100.</returns>
    public static StatPair ManagePossessionUpdates(
        int minute,
        IReadOnlyList<TimelineEvent> activeEvents,
        IReadOnlyList<MatchData>? checkpoints = null,
        StatPair? targetFinalPossession = null)
    {
        double homePos;

        // 1. If predefined synthetic interval checkpoints are available, interpolate smoothly between them
        if (checkpoints is { Count: > 0 })
        {
            var ordered = checkpoints.OrderBy(c => c.CurrentMinute).ToList();
            if (minute <= ordered[0].CurrentMinute)
            {
                // Smooth interpolation from opening 58% at kickoff to first checkpoint (e.g. 62% at 5')
                var first = ordered[0];
                const double kickoffHome = 58.0;
                var t = (double)minute / Math.Max(1, first.CurrentMinute);
                homePos = kickoffHome + t * (first.Statistics.Possession.Home - kickoffHome);
            }
            else if (minute >= ordered[^1].CurrentMinute)
            {
                homePos = ordered[^1].Statistics.Possession.Home;
            }
            else
            {
                // Find bounding checkpoints c1 <= minute <= c2
                var c1 = ordered.Last(c => c.CurrentMinute <= minute);
                var c2 = ordered.First(c => c.CurrentMinute > minute);
                var span = Math.Max(1, c2.CurrentMinute - c1.CurrentMinute);
                var t = (double)(minute - c1.CurrentMinute) / span;
                homePos = c1.Statistics.Possession.Home + t * (c2.Statistics.Possession.Home - c1.Statistics.Possession.Home);
            }
        }
        else
        {
            // Fallback parametric possession curve if no checkpoints are supplied
            var baselineHome = targetFinalPossession?.Home ?? 54.0;
            // Early Arsenal dominance (62% at 10'), mid-match Liverpool surge (50% at 30'), late Arsenal revival (60% at 85')
            if (minute < 15)
            {
                homePos = 58.0 + (minute / 15.0) * 4.0;
            }
            else if (minute < 35)
            {
                homePos = 62.0 - ((minute - 15.0) / 20.0) * 12.0;
            }
            else if (minute < 55)
            {
                homePos = 50.0 + ((minute - 35.0) / 20.0) * 5.0;
            }
            else
            {
                homePos = 55.0 + ((minute - 55.0) / 35.0) * (baselineHome - 55.0);
            }
        }

        // 2. Dynamic Event-Driven Micro-Adjustments (Pressure Sequences, Red Cards, High-Danger Sequences)
        double eventDelta = 0.0;

        // Disciplinary Red Card Impact: having a numerical advantage heavily tilts sustained territorial control
        var homeRedCards = activeEvents.Count(e =>
            e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals("ARS", StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals("Arsenal", StringComparison.OrdinalIgnoreCase)));

        var awayRedCards = activeEvents.Count(e =>
            e.EventType.Equals("RedCard", StringComparison.OrdinalIgnoreCase) &&
            (e.Team.Equals("LIV", StringComparison.OrdinalIgnoreCase) ||
             e.Team.Equals("Liverpool", StringComparison.OrdinalIgnoreCase)));

        // Net red cards (+1 means away team is down a man -> home gets possession boost)
        var netManAdvantage = awayRedCards - homeRedCards;
        eventDelta += netManAdvantage * 2.5;

        // Recent Pressure Sequences (within the last 3 match minutes)
        var recentPressure = activeEvents
            .Where(e => e.Minute >= minute - 3 && e.Minute <= minute &&
                        e.EventType.Equals("PressureSequence", StringComparison.OrdinalIgnoreCase));

        foreach (var seq in recentPressure)
        {
            var isHome = seq.Team.Equals("ARS", StringComparison.OrdinalIgnoreCase) ||
                         seq.Team.Equals("Arsenal", StringComparison.OrdinalIgnoreCase);
            var durationFactor = Math.Clamp((seq.DurationSeconds ?? 60) / 60.0, 0.5, 2.0);
            eventDelta += (isHome ? 1.5 : -1.5) * durationFactor;
        }

        // 3. Final Clamping and Normalization (Ensuring Home + Away == 100)
        var finalHome = (int)Math.Round(homePos + eventDelta);
        finalHome = Math.Clamp(finalHome, 25, 75);
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
            checkpoints,
            master.Statistics?.Possession);

        // Scale cumulative statistics proportionally to the elapsed match minute, reaching the exact final stats at 90'
        MatchStats statsAtMinute;
        if (minute >= 90)
        {
            statsAtMinute = master.Statistics with { Possession = dynamicPossession };
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

            var homeSOT = Math.Max(homeShotsOnTargetEvents, (int)Math.Round(master.Statistics.ShotsOnTarget.Home * ratio));
            var awaySOT = Math.Max(awayShotsOnTargetEvents, (int)Math.Round(master.Statistics.ShotsOnTarget.Away * ratio));
            var homeShots = Math.Max(homeSOT, (int)Math.Round(master.Statistics.Shots.Home * ratio));
            var awayShots = Math.Max(awaySOT, (int)Math.Round(master.Statistics.Shots.Away * ratio));

            statsAtMinute = new MatchStats(
                Possession: dynamicPossession,
                Shots: new StatPair(homeShots, awayShots),
                ShotsOnTarget: new StatPair(homeSOT, awaySOT),
                PassAccuracy: master.Statistics.PassAccuracy,
                Corners: new StatPair(
                    (int)Math.Round(master.Statistics.Corners.Home * ratio),
                    (int)Math.Round(master.Statistics.Corners.Away * ratio)),
                Fouls: new StatPair(
                    (int)Math.Round(master.Statistics.Fouls.Home * ratio),
                    (int)Math.Round(master.Statistics.Fouls.Away * ratio))
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

    private async Task RefreshFromBlobStorageSilentlyAsync()
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
            // Ignore local cache write errors
        }

        _logger.LogInformation(
            "Successfully downloaded '{BlobName}' from Azure Blob container '{ContainerName}'.",
            blobName,
            containerName);

        var (master, snapshots) = ParseMatchPayload(json);
        _syntheticSnapshots = snapshots;
        return master;
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
            "score": 4,
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
            "score": 3,
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
              { "playerId": "LIV11", "name": "Darwin Nunez", "number": 9, "ST": "ST", "position": "ST", "captain": false }
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
            "possession": { "home": 60, "away": 40 },
            "shots": { "home": 16, "away": 12 },
            "shotsOnTarget": { "home": 7, "away": 6 },
            "passAccuracy": { "home": 88, "away": 84 },
            "corners": { "home": 8, "away": 5 },
            "fouls": { "home": 9, "away": 11 }
          },
          "timeline": [
            { "eventId": "EV001", "minute": 4, "eventType": "Shot", "team": "ARS", "player": "Bukayo Saka", "onTarget": true },
            { "eventId": "EV002", "minute": 7, "eventType": "PressureSequence", "team": "ARS", "durationSeconds": 85, "importanceScore": 75 },
            { "eventId": "EV003", "minute": 9, "eventType": "Goal", "team": "ARS", "player": "Gabriel Martinelli", "assist": "Martin Odegaard", "importanceScore": 100 },
            { "eventId": "EV004", "minute": 13, "eventType": "YellowCard", "team": "LIV", "player": "Alexis Mac Allister" },
            { "eventId": "EV005", "minute": 15, "eventType": "Shot", "team": "LIV", "player": "Darwin Nunez" },
            { "eventId": "EV006", "minute": 18, "eventType": "Penalty", "team": "LIV", "player": "Mohamed Salah" },
            { "eventId": "EV007", "minute": 18, "eventType": "YellowCard", "team": "ARS", "player": "Gabriel Magalhaes" },
            { "eventId": "EV008", "minute": 19, "eventType": "Goal", "team": "LIV", "player": "Mohamed Salah", "assist": "Penalty", "importanceScore": 95 },
            { "eventId": "EV009", "minute": 22, "eventType": "PressureSequence", "team": "LIV", "durationSeconds": 92, "importanceScore": 80 },
            { "eventId": "EV010", "minute": 24, "eventType": "Goal", "team": "LIV", "player": "Luis Diaz", "assist": "Trent Alexander-Arnold", "importanceScore": 95 },
            { "eventId": "EV011", "minute": 28, "eventType": "YellowCard", "team": "ARS", "player": "Riccardo Calafiori" },
            { "eventId": "EV012", "minute": 30, "eventType": "Shot", "team": "LIV", "player": "Dominik Szoboszlai" },
            { "eventId": "EV013", "minute": 33, "eventType": "YellowCard", "team": "LIV", "player": "Ibrahima Konate" },
            { "eventId": "EV014", "minute": 35, "eventType": "RedCard", "team": "LIV", "player": "Ibrahima Konate", "importanceScore": 90 },
            { "eventId": "EV015", "minute": 37, "eventType": "Substitution", "team": "LIV" },
            { "eventId": "EV016", "minute": 39, "eventType": "Goal", "team": "ARS", "player": "Bukayo Saka", "assist": "Declan Rice", "importanceScore": 100 },
            { "eventId": "EV017", "minute": 43, "eventType": "YellowCard", "team": "ARS", "player": "Mikel Merino" },
            { "eventId": "EV018", "minute": 45, "eventType": "RedCard", "team": "ARS", "player": "Mikel Merino", "importanceScore": 90 },
            { "eventId": "EV019", "minute": 45, "eventType": "Shot", "team": "ARS", "player": "Martin Odegaard" },
            { "eventId": "EV020", "minute": 46, "eventType": "Substitution", "team": "ARS" },
            { "eventId": "EV021", "minute": 49, "eventType": "Shot", "team": "LIV", "player": "Mohamed Salah" },
            { "eventId": "EV022", "minute": 52, "eventType": "PressureSequence", "team": "LIV", "durationSeconds": 78, "importanceScore": 75 },
            { "eventId": "EV023", "minute": 54, "eventType": "Goal", "team": "LIV", "player": "Darwin Nunez", "assist": "Mohamed Salah", "importanceScore": 95 },
            { "eventId": "EV024", "minute": 58, "eventType": "YellowCard", "team": "LIV", "player": "Ryan Gravenberch" },
            { "eventId": "EV025", "minute": 60, "eventType": "Shot", "team": "ARS", "player": "Kai Havertz" },
            { "eventId": "EV026", "minute": 62, "eventType": "Substitution", "team": "ARS" },
            { "eventId": "EV027", "minute": 64, "eventType": "Substitution", "team": "LIV" },
            { "eventId": "EV028", "minute": 67, "eventType": "PressureSequence", "team": "ARS", "durationSeconds": 110, "importanceScore": 85 },
            { "eventId": "EV029", "minute": 69, "eventType": "Penalty", "team": "ARS", "player": "Bukayo Saka" },
            { "eventId": "EV030", "minute": 69, "eventType": "YellowCard", "team": "LIV", "player": "Andy Robertson" },
            { "eventId": "EV031", "minute": 70, "eventType": "Goal", "team": "ARS", "player": "Bukayo Saka", "assist": "Penalty", "importanceScore": 100 },
            { "eventId": "EV032", "minute": 73, "eventType": "Substitution", "team": "LIV" },
            { "eventId": "EV033", "minute": 75, "eventType": "Shot", "team": "ARS", "player": "Leandro Trossard" },
            { "eventId": "EV034", "minute": 78, "eventType": "YellowCard", "team": "ARS", "player": "Declan Rice" },
            { "eventId": "EV035", "minute": 80, "eventType": "PressureSequence", "team": "ARS", "durationSeconds": 124, "importanceScore": 90 },
            { "eventId": "EV036", "minute": 83, "eventType": "Shot", "team": "ARS", "player": "Martin Odegaard" },
            { "eventId": "EV037", "minute": 85, "eventType": "Goal", "team": "ARS", "player": "Kai Havertz", "assist": "Leandro Trossard", "importanceScore": 100 },
            { "eventId": "EV038", "minute": 88, "eventType": "Substitution", "team": "ARS" },
            { "eventId": "EV039", "minute": 89, "eventType": "YellowCard", "team": "LIV", "player": "Virgil van Dijk" },
            { "eventId": "EV040", "minute": 90, "eventType": "Shot", "team": "LIV", "player": "Diogo Jota" }
          ],
          "generatedInsights": [
            {
              "minute": 9,
              "type": "GoalImpact",
              "summary": "Gabriel Martinelli opens the scoring early for Arsenal following Odegaard's threaded pass.",
              "reason": "Odegaard found space between the lines to release Martinelli behind Alexander-Arnold."
            },
            {
              "minute": 19,
              "type": "MomentumShift",
              "summary": "Mohamed Salah calmly converts from the penalty spot to level the scores at 1-1.",
              "reason": "Salah drew a late tackle in the penalty box and dispatched it to the bottom-right corner."
            },
            {
              "minute": 24,
              "type": "TacticalAdvantage",
              "summary": "Luis Diaz fires Liverpool ahead 2-1 after Trent Alexander-Arnold's precision cross.",
              "reason": "A devastating rapid transition down Liverpool's right flank caught Arsenal's backline stepping up."
            },
            {
              "minute": 35,
              "type": "DisciplinaryImpact",
              "summary": "Ibrahima Konate is sent off with a second yellow card, reducing Liverpool to 10 men.",
              "reason": "A cynical foul to halt a promising Arsenal counter-attack leaves Liverpool with a numerical deficit."
            },
            {
              "minute": 39,
              "type": "GoalImpact",
              "summary": "Bukayo Saka equalizes 2-2 for Arsenal before the break.",
              "reason": "Exploiting the space vacated after Konate's dismissal, Declan Rice found Saka on the edge of the six-yard box."
            },
            {
              "minute": 45,
              "type": "DisciplinaryImpact",
              "summary": "Mikel Merino receives a straight red card for Arsenal, leveling the match at 10 vs 10.",
              "reason": "An aggressive challenge in midfield prompts the referee to brandish red on the stroke of halftime."
            },
            {
              "minute": 54,
              "type": "MomentumShift",
              "summary": "Darwin Nunez restores Liverpool's lead to 3-2 with a clinical finish.",
              "reason": "Salah held up possession and slipped Nunez through the seam in Arsenal's defense."
            },
            {
              "minute": 70,
              "type": "GoalImpact",
              "summary": "Bukayo Saka scores his second from the spot to bring Arsenal level at 3-3.",
              "reason": "Saka earned the penalty following sustained Arsenal pressure and sent Alisson the wrong way."
            },
            {
              "minute": 85,
              "type": "WinningMoment",
              "summary": "Kai Havertz scores an 85th-minute winner to give Arsenal a sensational 4-3 lead.",
              "reason": "Leandro Trossard's whipped cross was met emphatically by Havertz inside the six-yard box."
            }
          ]
        }
        """;
        return JsonSerializer.Deserialize<MatchData>(fallbackJson, _jsonOptions)!;
    }
}
