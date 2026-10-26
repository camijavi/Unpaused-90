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

            var isSpanish = string.Equals(request.Language?.Trim(), "es", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(request.Language?.Trim(), "es-ES", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(request.Language?.Trim(), "es-MX", StringComparison.OrdinalIgnoreCase);

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var response = await speechService.GenerateAudioUrlAsync(request, linkedCts.Token);
                return Results.Ok(response);
            }
            catch
            {
                // Graceful fallback response: return 200 OK with AudioUrl = null so client uses native speech without 500 errors
                return Results.Ok(new AudioResponse(
                    AudioUrl: null,
                    Voice: request.Voice ?? (isSpanish ? "es-MX-DaliaNeural" : "en-GB-OllieMultilingualNeural"),
                    Language: isSpanish ? "es" : "en"
                ));
            }
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

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var (audioBytes, _, _) = await speechService.SynthesizeSpeechBytesAsync(request, linkedCts.Token);
                return Results.File(audioBytes, "audio/mpeg", "unpaused90-recap.mp3");
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        })
        .WithName("GenerateAudioStream");
    }
}
