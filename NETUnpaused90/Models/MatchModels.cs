namespace NETUnpaused90.Models;

public record MatchData(
    string MatchId,
    string Competition,
    string Season,
    string Stadium,
    string Status,
    int CurrentMinute,
    Team HomeTeam,
    Team AwayTeam,
    MatchStats Statistics,
    List<TimelineEvent> Timeline,
    List<GeneratedInsight> GeneratedInsights
);

public record Team(
    string TeamId,
    string Name,
    string Coach, 
    string Formation,
    int Score,
    List<Player> StartingXI,
    List<SubstitutePlayer> Substitutes
);

public record Player(
    string PlayerId,
    string Name,
    int Number,
    string Position,
    bool Captain
);

public record SubstitutePlayer(
    string PlayerId,
    string Name,
    int Number,
    string Position
);

public record MatchStats(
    StatPair Possession,
    StatPair Shots,
    StatPair ShotsOnTarget,
    StatPair PassAccuracy,
    StatPair Corners,
    StatPair Fouls
);

public record StatPair(
    int Home,
    int Away
);

public record TimelineEvent(
    string EventId,
    int Minute,
    string EventType,
    string Team,
    string? Player = null,
    string? Assist = null,
    bool? OnTarget = null,
    int? DurationSeconds = null,
    int? ImportanceScore = null,
    string? PlayerIn = null,
    string? PlayerOut = null
);

public record GeneratedInsight(
    int Minute,
    string Type,
    string Summary,
    string Reason
);

// DTOs for the API
public record MatchSummaryDto(
    string MatchId,
    string Competition,
    string Stadium,
    string HomeTeam,
    string AwayTeam,
    int HomeScore,
    int AwayScore,
    string Score,
    int CurrentMinute,
    string Status
);

public record CatchUpRequest (
    string MatchId,
    int LastViewedMinute,
    int CurrentMinute,
    string ViewMode,
    string? FavoriteTeam
);

public record CatchUpResponse (
    string WhatHappened,
    string WhyItMatters,
    string MomentumChanges,
    string WhatToWatchNext
);
