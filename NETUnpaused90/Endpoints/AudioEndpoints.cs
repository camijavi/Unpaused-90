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
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
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
                // Neural TTS for a full multi-paragraph recap (especially Spanish, which is ~25% longer) routinely
                // takes several seconds. A short timeout silently forced the robotic browser voice fallback.
                using var timeoutCts = new CancellationTokenSource(GetSynthesisTimeout(configuration));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var response = await speechService.GenerateAudioUrlAsync(request, linkedCts.Token);
                return Results.Ok(response);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                loggerFactory.CreateLogger("AudioEndpoints").LogWarning(
                    ex,
                    "Azure neural speech synthesis failed ({Language}); client will fall back to native speech.",
                    isSpanish ? "es" : "en");

                // Graceful fallback response: return 200 OK with AudioUrl = null so client uses native speech without 500 errors.
                // Keep the persona voice (Ollie/Ada) so the UI highlights the correct co-host.
                return Results.Ok(new AudioResponse(
                    AudioUrl: null,
                    Voice: request.Voice ?? "en-GB-OllieMultilingualNeural",
                    Language: isSpanish ? "es" : "en"
                ));
            }
        })
        .WithName("GenerateAudio");

        // POST /api/v1/audio/stream — Returns raw audio/mpeg MP3 stream
        group.MapPost("/audio/stream", async (
            AudioRequest request,
            SpeechService speechService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(new { error = "Text is required to generate audio." });
            }

            try
            {
                using var timeoutCts = new CancellationTokenSource(GetSynthesisTimeout(configuration));
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

    private static TimeSpan GetSynthesisTimeout(IConfiguration configuration)
    {
        var seconds = configuration.GetValue<double?>("AzureSpeech:TimeoutSeconds") ?? 30;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 120));
    }
}
