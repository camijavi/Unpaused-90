using NETUnpaused90.Models;
using NETUnpaused90.Services;

namespace NETUnpaused90.Endpoints;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("AI Intelligence");

        // POST /api/v1/catchup — Hero endpoint: explains what the fan missed between lastViewedMinute and currentMinute
        group.MapPost("/catchup", async (
            CatchUpRequest request,
            AiService aiService,
            CancellationToken ct) =>
        {
            var result = await aiService.GenerateCatchUpAsync(request, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.NotFound(new { error = $"Match '{request.MatchId}' not found." });
        })
        .WithName("GenerateCatchUp");

        // POST /api/v1/personalized-summary — Generates full or halftime recap tailored to mode & favorite team
        group.MapPost("/personalized-summary", async (
            PersonalizedSummaryRequest request,
            AiService aiService,
            CancellationToken ct) =>
        {
            var result = await aiService.GeneratePersonalizedSummaryAsync(request, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.NotFound(new { error = $"Match '{request.MatchId}' not found." });
        })
        .WithName("GeneratePersonalizedSummary");

        // POST /api/v1/explain — Expandable "Why It Matters" explanation for a specific timeline event
        group.MapPost("/explain", async (
            ExplainEventRequest request,
            AiService aiService,
            CancellationToken ct) =>
        {
            var result = await aiService.ExplainEventAsync(request, ct);
            return result is not null
                ? Results.Ok(result)
                : Results.NotFound(new { error = $"Match '{request.MatchId}' or Event '{request.EventId}' not found." });
        })
        .WithName("ExplainEvent");
    }
}
