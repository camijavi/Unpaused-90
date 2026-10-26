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
    private const string MaleSpanishVoice = "es-MX-JorgeNeural";
    private const string FemaleSpanishVoice = "es-MX-DaliaNeural";

    private readonly IConfiguration _configuration;
    private readonly ILogger<SpeechService> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _resourceIdCachePath;
    private string? _cachedResourceId;
    private int _voiceTurnCounter;

    public SpeechService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<SpeechService> logger,
        HttpClient httpClient,
        TokenCredential credential)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
        _credential = credential;

        var cacheDir = Path.Combine(env.ContentRootPath, "obj");
        Directory.CreateDirectory(cacheDir);
        _resourceIdCachePath = Path.Combine(cacheDir, "speech-resource-id.txt");
        if (File.Exists(_resourceIdCachePath))
        {
            var saved = File.ReadAllText(_resourceIdCachePath).Trim();
            if (!string.IsNullOrWhiteSpace(saved))
            {
                _cachedResourceId = saved;
            }
        }
    }

    /// <summary>
    /// Picks the next alternating voice (Ollie on odd turns, Ada on even turns) for a newly generated summary.
    /// </summary>
    public string NextAlternatingVoice()
    {
        var maleVoice = _configuration["AzureSpeech:MaleVoiceName"] ?? MaleBritishVoice;
        var femaleVoice = _configuration["AzureSpeech:FemaleVoiceName"] ?? FemaleBritishVoice;
        var turn = Interlocked.Increment(ref _voiceTurnCounter);
        return (turn % 2 == 1) ? maleVoice : femaleVoice;
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
        var isSpanish = string.Equals(request.Language?.Trim(), "es", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-ES", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-MX", StringComparison.OrdinalIgnoreCase);

        var xmlLang = isSpanish ? "es-MX" : "en-GB";

        // Persona voice (Ollie / Ada) — always returned to the client so the UI highlights the right co-host.
        var selectedVoice = !string.IsNullOrWhiteSpace(request.Voice)
            ? request.Voice.Trim()
            : NextAlternatingVoice();

        // English multilingual voices speaking Spanish sound accented/flat. Use native Spanish neural voices
        // mapped to the same persona gender for natural Spanish delivery.
        var synthesisVoice = isSpanish ? ResolveSpanishVoice(selectedVoice) : selectedVoice;

        var paragraphs = request.Text
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(request.Text.Trim());
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
            "Synthesizing audio using voice '{VoiceName}' (persona '{Persona}', {Language})...",
            synthesisVoice,
            selectedVoice,
            xmlLang);

        var ssml = BuildNaturalSsml(paragraphs, synthesisVoice, xmlLang, isSpanish);
        var audioBytes = await SynthesizeSingleSegmentAsync(ttsUrl, ssml, apiKey, authHeaderValue, cancellationToken);

        return (audioBytes, selectedVoice, isSpanish ? "es" : "en");
    }

    private async Task<byte[]> SynthesizeSingleSegmentAsync(
        string ttsUrl,
        string ssml,
        string? apiKey,
        string? aadBearerToken,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
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
            if ((int)response.StatusCode == 429 && attempt == 1)
            {
                await Task.Delay(600, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Azure AI Speech returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        throw new InvalidOperationException("Azure AI Speech synthesis failed after retry.");
    }

    /// <summary>
    /// Maps a persona voice (Ollie = male, Ada = female) to a native Spanish neural voice.
    /// Configurable via AzureSpeech:SpanishMaleVoiceName / AzureSpeech:SpanishFemaleVoiceName.
    /// </summary>
    private string ResolveSpanishVoice(string personaVoice)
    {
        // A caller explicitly asking for a Spanish voice gets it as-is.
        if (personaVoice.StartsWith("es-", StringComparison.OrdinalIgnoreCase))
        {
            return personaVoice;
        }

        var femaleVoice = _configuration["AzureSpeech:FemaleVoiceName"] ?? FemaleBritishVoice;
        var isFemale = personaVoice.Contains("Ada", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(personaVoice, femaleVoice, StringComparison.OrdinalIgnoreCase);

        return isFemale
            ? _configuration["AzureSpeech:SpanishFemaleVoiceName"] ?? FemaleSpanishVoice
            : _configuration["AzureSpeech:SpanishMaleVoiceName"] ?? MaleSpanishVoice;
    }

    private static string BuildNaturalSsml(List<string> paragraphs, string voiceName, string xmlLang, bool isSpanish)
    {
        return isSpanish
            ? BuildSpanishSsml(paragraphs, voiceName, xmlLang)
            : BuildUnifiedEmotionalSsml(paragraphs, voiceName, xmlLang);
    }

    /// <summary>
    /// Spanish SSML: native voice, one continuous prosody envelope and natural pauses.
    /// Per-sentence pitch/rate jumps were the main source of the "robotic" Spanish delivery, so the
    /// neural model is left to drive intonation from punctuation (¡…! / ¿…?) on its own.
    /// </summary>
    private static string BuildSpanishSsml(List<string> paragraphs, string voiceName, string xmlLang)
    {
        var sb = new StringBuilder();
        sb.Append($"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='https://www.w3.org/2001/mstts' xml:lang='{xmlLang}'>");
        sb.Append($"<voice name='{voiceName}'>");
        sb.Append("<prosody rate='+4%'>");

        for (int pIdx = 0; pIdx < paragraphs.Count; pIdx++)
        {
            var escaped = SecurityElement.Escape(paragraphs[pIdx].Trim()) ?? string.Empty;
            sb.Append(escaped);

            if (pIdx < paragraphs.Count - 1)
            {
                sb.Append(" <break time='450ms'/> ");
            }
        }

        sb.Append("</prosody>");
        sb.Append("</voice>");
        sb.Append("</speak>");

        return sb.ToString();
    }

    private static string BuildUnifiedEmotionalSsml(List<string> paragraphs, string voiceName, string xmlLang)
    {
        var sb = new StringBuilder();
        sb.Append($"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='https://www.w3.org/2001/mstts' xml:lang='{xmlLang}'>");
        sb.Append($"<voice name='{voiceName}'>");
        sb.Append($"<mstts:express-as style='excited' styledegree='1.4'>");
        sb.Append($"<lang xml:lang='{xmlLang}'>");

        for (int pIdx = 0; pIdx < paragraphs.Count; pIdx++)
        {
            var sentences = Regex.Split(paragraphs[pIdx].Trim(), @"(?<=[.!?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

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

            if (pIdx < paragraphs.Count - 1)
            {
                sb.Append("<break time='320ms'/> ");
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
                        try
                        {
                            await File.WriteAllTextAsync(_resourceIdCachePath, _cachedResourceId, cancellationToken);
                        }
                        catch
                        {
                            // Ignore disk cache write errors
                        }
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
