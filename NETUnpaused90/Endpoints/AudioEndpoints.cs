using NETUnpaused90.Models;
using NETUnpaused90.Services;

namespace NETUnpaused90.Endpoints;

public static class AudioEndpoints
{
    public static void MapAudioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Audio Recap");

        // POST /api/v1/audio — Returns JSON { "audioUrl": "data:audio/mpeg;base64,...", "voice": "...", "language": "..." }
        group.MapPost("/audio", async (
            AudioRequest request,
            SpeechService speechService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(new { error = "Text is required to generate audio." });
            }

            var response = await speechService.GenerateAudioUrlAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GenerateAudio");

        // POST /api/v1/audio/stream — Returns raw audio/mpeg MP3 stream
        group.MapPost("/audio/stream", async (
            AudioRequest request,
            SpeechService speechService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(new { error = "Text is required to generate audio." });
            }

            var (audioBytes, _, _) = await speechService.SynthesizeSpeechBytesAsync(request, ct);
            return Results.File(audioBytes, "audio/mpeg", "unpaused90-recap.mp3");
        })
        .WithName("GenerateAudioStream");
    }
}
