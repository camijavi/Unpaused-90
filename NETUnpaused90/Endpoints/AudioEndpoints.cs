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

            var isSpanish = string.Equals(request.Language?.Trim(), "es-MX", StringComparison.OrdinalIgnoreCase);
            // string.Equals(request.Language?.Trim(), "es", StringComparison.OrdinalIgnoreCase) ||
            //                 string.Equals(request.Language?.Trim(), "es-ES", StringComparison.OrdinalIgnoreCase) ||


            var resolvedVoice = !string.IsNullOrWhiteSpace(request.Voice)
                ? request.Voice.Trim()
                : speechService.NextAlternatingVoice();
            var effectiveRequest = request with { Voice = resolvedVoice };

            try
            {
                // Neural TTS for a full multi-paragraph recap (especially Spanish, which is ~25% longer) routinely
                // takes several seconds. Scale timeout dynamically with script length so longer narratives never abort prematurely.
                using var timeoutCts = new CancellationTokenSource(GetSynthesisTimeout(configuration, request.Text.Length));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var response = await speechService.GenerateAudioUrlAsync(effectiveRequest, linkedCts.Token);
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
                    Voice: resolvedVoice,
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

            var resolvedVoice = !string.IsNullOrWhiteSpace(request.Voice)
                ? request.Voice.Trim()
                : speechService.NextAlternatingVoice();
            var effectiveRequest = request with { Voice = resolvedVoice };

            try
            {
                using var timeoutCts = new CancellationTokenSource(GetSynthesisTimeout(configuration, request.Text.Length));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var (audioBytes, _, _) = await speechService.SynthesizeSpeechBytesAsync(effectiveRequest, linkedCts.Token);
                return Results.File(audioBytes, "audio/mpeg", "unpaused90-recap.mp3");
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        })
        .WithName("GenerateAudioStream");
    }

    private static TimeSpan GetSynthesisTimeout(IConfiguration configuration, int textLength = 0)
    {
        var configuredSeconds = configuration.GetValue<double?>("AzureSpeech:TimeoutSeconds");
        if (configuredSeconds.HasValue)
        {
            return TimeSpan.FromSeconds(Math.Clamp(configuredSeconds.Value, 5, 120));
        }

        // Dynamically scale timeout based on narrative length so 5m and full summaries synthesize smoothly
        var defaultSeconds = textLength switch
        {
            > 2000 => 90.0,
            > 800 => 60.0,
            _ => 35.0
        };

        return TimeSpan.FromSeconds(defaultSeconds);
    }
}
