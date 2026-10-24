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
    private const string MaleBritishVoice = "en-GB-OllieMultilingualNeural";
    private const string FemaleBritishVoice = "en-GB-AdaMultilingualNeural";

    private readonly IConfiguration _configuration;
    private readonly ILogger<SpeechService> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private string? _cachedResourceId;
    private int _voiceTurnCounter;

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
        var maleVoice = _configuration["AzureSpeech:MaleVoiceName"] ?? MaleBritishVoice;
        var femaleVoice = _configuration["AzureSpeech:FemaleVoiceName"] ?? FemaleBritishVoice;

        var isSpanish = string.Equals(request.Language?.Trim(), "es", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-ES", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-MX", StringComparison.OrdinalIgnoreCase);

        var xmlLang = isSpanish ? "es-MX" : "en-GB";

        // Alternate between Ollie (Male) and Ada (Female) on each generated summary/audio!
        var turn = Interlocked.Increment(ref _voiceTurnCounter);
        var selectedVoice = (turn % 2 == 1) ? maleVoice : femaleVoice;

        // Split paragraphs so we can synthesize them in PARALLEL (Task.WhenAll) using the SAME voice for speed
        var segments = request.Text
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (segments.Count == 0)
        {
            segments.Add(request.Text.Trim());
        }

        string? authHeaderValue = null;
        var apiKey = _configuration["AzureSpeech:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("<"))
        {
            var tokenTask = _credential.GetTokenAsync(
                new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                cancellationToken).AsTask();
            var resourceIdTask = GetSpeechResourceIdAsync(cancellationToken);

            await Task.WhenAll(tokenTask, resourceIdTask);
            authHeaderValue = $"aad#{resourceIdTask.Result}#{tokenTask.Result.Token}";
        }

        var ttsUrl = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";

        _logger.LogInformation(
            "Synthesizing audio summary using voice '{VoiceName}' ({Language}) across {SegmentCount} parallel chunk(s)...",
            selectedVoice,
            xmlLang,
            segments.Count);

        var segmentTasks = segments.Select(segmentText =>
        {
            var ssml = BuildEmotionalSegmentSsml(segmentText, selectedVoice, xmlLang);
            return SynthesizeSingleSegmentAsync(ttsUrl, ssml, apiKey, authHeaderValue, cancellationToken);
        }).ToArray();

        var audioChunks = await Task.WhenAll(segmentTasks);

        var totalBytes = audioChunks.Sum(c => c.Length);
        using var combinedStream = new MemoryStream(totalBytes);
        foreach (var chunk in audioChunks)
        {
            combinedStream.Write(chunk, 0, chunk.Length);
        }

        return (combinedStream.ToArray(), selectedVoice, isSpanish ? "es" : "en");
    }

    private async Task<byte[]> SynthesizeSingleSegmentAsync(
        string ttsUrl,
        string ssml,
        string? apiKey,
        string? aadBearerToken,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ttsUrl)
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml")
        };

        httpRequest.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-96kbitrate-mono-mp3");
        httpRequest.Headers.Add("User-Agent", "Unpaused90App");

        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("<"))
        {
            httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
        }
        else if (!string.IsNullOrWhiteSpace(aadBearerToken))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aadBearerToken);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Azure AI Speech returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Builds energetic, emotionally dynamic SSML for the selected British Multilingual voice (Ollie or Ada),
    /// modulating pitch, speaking rate, and emphasis sentence-by-sentence so it sounds like live commentary.
    /// </summary>
    private static string BuildEmotionalSegmentSsml(string segmentText, string voiceName, string xmlLang)
    {
        var sentences = Regex.Split(segmentText.Trim(), @"(?<=[.!?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var sb = new StringBuilder();
        sb.Append($"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='https://www.w3.org/2001/mstts' xml:lang='{xmlLang}'>");
        sb.Append($"<voice name='{voiceName}'>");
        sb.Append($"<mstts:express-as style='excited' styledegree='1.4'>");
        sb.Append($"<lang xml:lang='{xmlLang}'>");

        foreach (var rawSentence in sentences)
        {
            var escaped = SecurityElement.Escape(rawSentence.Trim()) ?? string.Empty;
            var isExclamation = rawSentence.EndsWith('!');
            var isQuestion = rawSentence.EndsWith('?');
            var isDramaticSigh = rawSentence.Contains("...");

            if (isExclamation)
            {
                sb.Append($"<prosody rate='+14%' pitch='+6%' volume='+12%'>{escaped}</prosody> ");
            }
            else if (isQuestion)
            {
                sb.Append($"<prosody rate='+11%' pitch='+5%'>{escaped}</prosody> ");
            }
            else if (isDramaticSigh)
            {
                sb.Append($"<prosody rate='+8%' pitch='-2%'>{escaped}</prosody> ");
            }
            else
            {
                sb.Append($"<prosody rate='+10%' pitch='+2%'>{escaped}</prosody> ");
            }
        }

        sb.Append("</lang>");
        sb.Append("</mstts:express-as>");
        sb.Append("</voice>");
        sb.Append("</speak>");

        return sb.ToString();
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
            "Please add 'AzureSpeech:ResourceId' in appsettings.json.");
    }
}
