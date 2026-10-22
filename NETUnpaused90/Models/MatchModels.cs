namespace NETUnpaused90.Models;

public record MatchData(
    string MatchId,
    string Competition,
    string Season,
    string Stadium,
    string Status,
    int CurrentTime,
    Team HomeTam,
    Team AwayTeam
);

public record Team(
    string TeamId,
    string Name,
    string Coach, 
    string Formation,
    int Score,
    List<Player> StartingXI,
    List <SubstitutePlayer> Substitutes
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
    StatPair Possesion,
    StatPair Shots,
    StatPair ShotsOnTarget,
    StatPair PassAcuracy,
    StatPair Corners,
    StatPair Fouls
);

public record StatPair(
    int Home,
    int Away
);

public record TimeLineEvenet(
    string EventId,
    int Minute,
    string EventType,
    string Team,
    string? Player,
    string? Assist,
    bool? OnTarget,
    int? DurationSeconds,
    int? ImportanceScore
);

public record GeneratedInSight(
    int Minute,
    string Type,
    string Summary,
    string Reason
);

// DTOs for the API
public record CatchUpRequest (
    string MatchId,
    int LastViewedMinute,
    int CurrentMinute,
    string ViewMode,
    string? FavoriteTeam
);

public record CatchUpResponse (
    string WhatHappend,
    string WhyItMatters,
    string MomentumChanges,
    string WhatToWatchNext
);

