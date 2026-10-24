using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using NETUnpaused90.Models;

namespace NETUnpaused90.Services;

public class SpeechService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SpeechService> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private string? _cachedResourceId;

    public SpeechService(
        IConfiguration configuration,
        ILogger<SpeechService> logger,
        HttpClient httpClient,
        TokenCredential credential)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
        _credential = credential;
    }

    public async Task<AudioResponse> GenerateAudioUrlAsync(
        AudioRequest request,
        CancellationToken cancellationToken = default)
    {
        var (audioBytes, voiceName, langCode) = await SynthesizeSpeechBytesAsync(request, cancellationToken);
        var base64Audio = Convert.ToBase64String(audioBytes);

        return new AudioResponse(
            AudioUrl: $"data:audio/mpeg;base64,{base64Audio}",
            Voice: voiceName,
            Language: langCode
        );
    }

    public async Task<(byte[] AudioBytes, string VoiceName, string Language)> SynthesizeSpeechBytesAsync(
        AudioRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Text cannot be empty.", nameof(request));
        }

        var region = _configuration["AzureSpeech:Region"] ?? "westus";
        var defaultVoice = _configuration["AzureSpeech:VoiceName"] ?? "en-US-AdamMultilingualNeural";

        var voiceName = NormalizeVoiceName(request.Voice, defaultVoice);
        var isSpanish = string.Equals(request.Language?.Trim(), "es", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-ES", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-MX", StringComparison.OrdinalIgnoreCase);

        var xmlLang = isSpanish ? "es-MX" : "en-US";
        var expressiveSsmlBody = BuildExpressiveSsmlBody(request.Text);

        // Use a natural, unhurried broadcast pace (-4% rate) with genuine SSML <break> pauses for emotion
        var ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{xmlLang}'>" +
            $"<voice name='{voiceName}'>" +
            $"<lang xml:lang='{xmlLang}'>" +
            $"<prosody rate='-4%'>{expressiveSsmlBody}</prosody>" +
            $"</lang>" +
            $"</voice>" +
            $"</speak>";

        var ttsUrl = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ttsUrl)
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml")
        };

        httpRequest.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-96kbitrate-mono-mp3");
        httpRequest.Headers.Add("User-Agent", "Unpaused90App");

        var apiKey = _configuration["AzureSpeech:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("<"))
        {
            httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
        }
        else
        {
            var tokenResult = await _credential.GetTokenAsync(
                new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                cancellationToken);

            var resourceId = await GetSpeechResourceIdAsync(cancellationToken);
            var aadBearerToken = $"aad#{resourceId}#{tokenResult.Token}";
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aadBearerToken);
        }

        _logger.LogInformation(
            "Calling Azure AI Speech TTS ({TtsUrl}) with voice '{VoiceName}' ({Language})...",
            ttsUrl,
            voiceName,
            xmlLang);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Azure AI Speech returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                $"Make sure your account has the 'Cognitive Services Speech User' role on 'unpaused-90-speech-service' in Azure Portal (IAM). Details: {errorBody}");
        }

        var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (audioBytes, voiceName, isSpanish ? "es" : "en");
    }

    /// <summary>
    /// Escapes raw text for XML and converts dramatic punctuation (..., —, !, paragraph breaks)
    /// into SSML &lt;break&gt; tags so the narration breathes and conveys real emotion.
    /// </summary>
    private static string BuildExpressiveSsmlBody(string rawText)
    {
        var escaped = SecurityElement.Escape(rawText) ?? string.Empty;

        // Paragraph / section breaks -> 750ms pause
        escaped = Regex.Replace(escaped, @"(\r?\n){2,}", " <break time='750ms'/> ");
        escaped = Regex.Replace(escaped, @"\r?\n", " <break time='450ms'/> ");

        // Ellipses (...) -> 600ms emotional pause (e.g. sighs, disbelief, building tension)
        escaped = Regex.Replace(escaped, @"\.{3,}", " <break time='600ms'/> ");

        // Em-dashes (— or --) -> 400ms dramatic beat
        escaped = escaped.Replace("—", " <break time='400ms'/> ")
                         .Replace(" -- ", " <break time='400ms'/> ")
                         .Replace(" - ", " <break time='300ms'/> ");

        // Sentence-ending exclamation or period -> natural breath pause
        escaped = Regex.Replace(escaped, @"!\s+", "! <break time='350ms'/> ");
        escaped = Regex.Replace(escaped, @"\?\s+", "? <break time='350ms'/> ");

        return escaped;
    }

    private static string NormalizeVoiceName(string? requestedVoice, string defaultVoice)
    {
        if (string.IsNullOrWhiteSpace(requestedVoice))
        {
            return defaultVoice;
        }

        var trimmed = requestedVoice.Trim();
        if (trimmed.Contains("AdamMultilingualNeural", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("damMultilingualNeural", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("SportsCommentator", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US-AdamMultilingualNeural";
        }

        return trimmed;
    }

    private async Task<string> GetSpeechResourceIdAsync(CancellationToken cancellationToken)
    {
        var configuredResourceId = _configuration["AzureSpeech:ResourceId"];
        if (!string.IsNullOrWhiteSpace(configuredResourceId) && !configuredResourceId.StartsWith("<"))
        {
            return configuredResourceId;
        }

        if (!string.IsNullOrWhiteSpace(_cachedResourceId))
        {
            return _cachedResourceId;
        }

        var resourceName = _configuration["AzureSpeech:ResourceName"] ?? "unpaused-90-speech-service";

        _logger.LogInformation(
            "Discovering Azure Resource ID for Speech service '{ResourceName}' via Microsoft Entra ID...",
            resourceName);

        var armToken = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            cancellationToken);

        using var subReq = new HttpRequestMessage(
            HttpMethod.Get,
            "https://management.azure.com/subscriptions?api-version=2022-12-01");
        subReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", armToken.Token);

        using var subRes = await _httpClient.SendAsync(subReq, cancellationToken);
        subRes.EnsureSuccessStatusCode();

        using var subDoc = await JsonDocument.ParseAsync(
            await subRes.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (subDoc.RootElement.TryGetProperty("value", out var subsArr))
        {
            foreach (var sub in subsArr.EnumerateArray())
            {
                if (!sub.TryGetProperty("subscriptionId", out var subIdProp))
                {
                    continue;
                }

                var subId = subIdProp.GetString();
                var filter = Uri.EscapeDataString($"name eq '{resourceName}'");
                var armUrl = $"https://management.azure.com/subscriptions/{subId}/resources?api-version=2021-04-01&$filter={filter}";

                using var armReq = new HttpRequestMessage(HttpMethod.Get, armUrl);
                armReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", armToken.Token);

                using var armRes = await _httpClient.SendAsync(armReq, cancellationToken);
                if (!armRes.IsSuccessStatusCode)
                {
                    continue;
                }

                using var doc = await JsonDocument.ParseAsync(
                    await armRes.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);

                if (doc.RootElement.TryGetProperty("value", out var valueArr) &&
                    valueArr.GetArrayLength() > 0 &&
                    valueArr[0].TryGetProperty("id", out var idProp))
                {
                    _cachedResourceId = idProp.GetString();
                    if (!string.IsNullOrWhiteSpace(_cachedResourceId))
                    {
                        _logger.LogInformation("Discovered Speech Resource ID: {ResourceId}", _cachedResourceId);
                        return _cachedResourceId;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not auto-discover Azure Resource ID for Speech resource '{resourceName}'. " +
            "Please add 'AzureSpeech:ResourceId' in appsettings.json (found in Azure Portal -> unpaused-90-speech-service -> Properties -> Resource ID).");
    }
}
