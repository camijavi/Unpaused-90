using NETUnpaused90.Services;

namespace NETUnpaused90.Endpoints;

public static class MatchEndpoints
{
    public static void MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/matches")
            .WithTags("Matches");

        // GET /api/v1/matches — Lists available synthetic matches with current simulated state
        group.MapGet("/", async (MatchService matchService, CancellationToken ct) =>
        {
            var matches = await matchService.GetAllMatchesAsync(ct);
            return Results.Ok(matches);
        })
        .WithName("GetMatches");

        // GET /api/v1/matches/{matchId} — Time-sliced match state at the current simulated minute (or optional ?minute=)
        group.MapGet("/{matchId}", async (
            string matchId,
            int? minute,
            MatchService matchService,
            CancellationToken ct) =>
        {
            var match = await matchService.GetMatchAsync(matchId, ct, minute);
            return match is not null
                ? Results.Ok(match)
                : Results.NotFound(new { error = $"Match '{matchId}' not found." });
        })
        .WithName("GetMatchById");

        // POST /api/v1/matches/{matchId}/simulation/reset — Starts/restarts live simulation at a random minute (1..90)
        group.MapPost("/{matchId}/simulation/reset", async (
            string matchId,
            int? startMinute,
            MatchService matchService,
            CancellationToken ct) =>
        {
            var match = await matchService.ResetSimulationAsync(matchId, startMinute, ct);
            return match is not null
                ? Results.Ok(match)
                : Results.NotFound(new { error = $"Match '{matchId}' not found." });
        })
        .WithName("ResetMatchSimulation");

        // GET /api/v1/matches/{matchId}/timeline — Chronological events up to current simulated minute
        group.MapGet("/{matchId}/timeline", async (
            string matchId,
            int? upToMinute,
            MatchService matchService,
            CancellationToken ct) =>
        {
            var timeline = await matchService.GetTimelineAsync(matchId, upToMinute, ct);
            return timeline is not null
                ? Results.Ok(timeline)
                : Results.NotFound(new { error = $"Match '{matchId}' not found." });
        })
        .WithName("GetMatchTimeline");

        // GET /api/v1/matches/{matchId}/insights — Key moments and "Why It Matters" insights up to current simulated minute
        group.MapGet("/{matchId}/insights", async (
            string matchId,
            int? upToMinute,
            MatchService matchService,
            CancellationToken ct) =>
        {
            var insights = await matchService.GetInsightsAsync(matchId, upToMinute, ct);
            return insights is not null
                ? Results.Ok(insights)
                : Results.NotFound(new { error = $"Match '{matchId}' not found." });
        })
        .WithName("GetMatchInsights");

        // Alias: GET /api/v1/matches/{matchId}/key-moments
        group.MapGet("/{matchId}/key-moments", async (
            string matchId,
            int? upToMinute,
            MatchService matchService,
            CancellationToken ct) =>
        {
            var insights = await matchService.GetInsightsAsync(matchId, upToMinute, ct);
            return insights is not null
                ? Results.Ok(insights)
                : Results.NotFound(new { error = $"Match '{matchId}' not found." });
        })
        .WithName("GetMatchKeyMoments");
    }
}
