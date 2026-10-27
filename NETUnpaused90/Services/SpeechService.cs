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
        var synthesisVoice = isSpanish ? ResolveSpanishVoice(selectedVoice) : ResolveEnglishVoice(selectedVoice);

        // Apply football-specific speech normalization specifications:
        // 1. 90' is never read as feet; it is verbalized as "90 minutes" or "minute 90".
        // 2. 2 - 3 in interval context is verbalized as "from minute 2 to minute 3" (del minuto 2 al minuto 3 in Spanish).
        // 3. Tactical formations (e.g. 4-3-3, 4-2-3-1, 3-5-2) are designated as formations, never confused with minutes or scores.
        var normalizedText = NormalizeFootballCommentaryForSpeech(request.Text, isSpanish);

        var paragraphs = normalizedText
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(request.Text.Trim());
        }

        var tokenTask = _credential.GetTokenAsync(
            new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
            cancellationToken).AsTask();
        var resourceIdTask = GetSpeechResourceIdAsync(cancellationToken);

        await Task.WhenAll(tokenTask, resourceIdTask);
        var authHeaderValue = $"aad#{resourceIdTask.Result}#{tokenTask.Result.Token}";

        var ttsUrl = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";

        _logger.LogInformation(
            "Synthesizing audio via Entra ID using voice '{VoiceName}' (persona '{Persona}', {Language})...",
            synthesisVoice,
            selectedVoice,
            xmlLang);

        var ssml = BuildNaturalSsml(paragraphs, synthesisVoice, xmlLang, isSpanish);
        var audioBytes = await SynthesizeSingleSegmentAsync(ttsUrl, ssml, authHeaderValue, cancellationToken);

        return (audioBytes, selectedVoice, isSpanish ? "es" : "en");
    }

    private async Task<byte[]> SynthesizeSingleSegmentAsync(
        string ttsUrl,
        string ssml,
        string aadBearerToken,
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
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aadBearerToken);

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

    /// <summary>
    /// Maps a persona name (Ollie / Ada / male / female) or short alias to a British English neural voice.
    /// Configurable via AzureSpeech:MaleVoiceName / AzureSpeech:FemaleVoiceName.
    /// </summary>
    private string ResolveEnglishVoice(string personaVoice)
    {
        if (personaVoice.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
        {
            return personaVoice;
        }

        var femaleVoice = _configuration["AzureSpeech:FemaleVoiceName"] ?? FemaleBritishVoice;
        var maleVoice = _configuration["AzureSpeech:MaleVoiceName"] ?? MaleBritishVoice;

        var isFemale = personaVoice.Contains("Ada", StringComparison.OrdinalIgnoreCase) ||
                       personaVoice.Contains("Female", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(personaVoice, femaleVoice, StringComparison.OrdinalIgnoreCase);

        return isFemale ? femaleVoice : maleVoice;
    }

    /// <summary>
    /// Normalizes football commentary and match timestamps so Azure AI Speech synthesizes them naturally:
    /// - 90' is never read as feet; it is verbalized as "90 minutes" or "minute 90".
    /// - Minute ranges like "2 - 3" or "2' - 3'" are verbalized as "from minute 2 to minute 3" (del minuto 2 al minuto 3 in Spanish).
    /// - Tactical formations (e.g. 4-3-3, 4-2-3-1, 3-5-2) are preserved and designated as formations, never confused with minutes or scores.
    /// </summary>
    public static string NormalizeFootballCommentaryForSpeech(string text, bool isSpanish)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var normalized = text;

        // 1. Protect tactical formations (e.g. 4-3-3, 4-2-3-1, 3-5-2)
        // Consume optional preceding or following formation words to avoid doubling
        var formationPlaceholders = new List<string>();
        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(?:formation\s+|formaci[oó]n\s+)?(\d)[-–—](\d)[-–—](\d)(?:[-–—](\d))?(?:\s+formation|\s+formaci[oó]n)?\b",
            m =>
            {
                var idx = formationPlaceholders.Count;
                var g4 = m.Groups[4].Success ? m.Groups[4].Value : null;
                var verbalized = isSpanish
                    ? (g4 != null
                        ? $"formación {m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value} {g4}"
                        : $"formación {m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}")
                    : (g4 != null
                        ? $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}-{g4} formation"
                        : $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value} formation");
                formationPlaceholders.Add(verbalized);
                return $"__FORMATION_{idx}__";
            });

        // 2. Protect match scores when preceded by score verbs/nouns (e.g. 'at 4-3', 'lead 2-1', 'won 3-2', 'marcador 2-1')
        var scorePlaceholders = new List<string>();
        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(score|scores|scored|lead|leads|led|trailing|won|win|wins|lost|lose|loses|beat|beats|ahead|draw|drew|drawn|tied|finish|finished|winning|level|at|ended|stands at|marcador|resultado|gan[oó]|ganando|gana|ganan|perdi[oó]|perdiendo|pierde|pierden|empate|empat[oó]|empatando|venci[oó]|venciendo|finaliz[oó]|termin[oó])\s+(\d{1,2})\s*[-–—]\s*(\d{1,2})\b",
            m =>
            {
                var idx = scorePlaceholders.Count;
                var verb = m.Groups[1].Value;
                var s1 = m.Groups[2].Value;
                var s2 = m.Groups[3].Value;
                var verbalized = isSpanish
                    ? $"{verb} {s1} a {s2}"
                    : $"{verb} {s1} to {s2}";
                scorePlaceholders.Add(verbalized);
                return $"__SCORE_{idx}__";
            });

        // Also catch tight single-digit scores like "4-3" or "2-1" that are not spaced out
        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(\d)\s*[-–—]\s*(\d)\b",
            m =>
            {
                // If it is spaced like "2 - 3", it's the minute range per user spec: "2 - 3 means 'from minute 2 to minute 3'"
                if (m.Value.Contains(' ') || m.Value.Contains(" - ") || m.Value.Contains(" – "))
                {
                    return isSpanish
                        ? $"del minuto {m.Groups[1].Value} al minuto {m.Groups[2].Value}"
                        : $"from minute {m.Groups[1].Value} to minute {m.Groups[2].Value}";
                }

                // Tight like "4-3" or "2-1" is a score: "4 to 3" / "4 a 3"
                var idx = scorePlaceholders.Count;
                var verbalized = isSpanish
                    ? $"{m.Groups[1].Value} a {m.Groups[2].Value}"
                    : $"{m.Groups[1].Value} to {m.Groups[2].Value}";
                scorePlaceholders.Add(verbalized);
                return $"__SCORE_{idx}__";
            });

        // 3. Convert explicit minute ranges: '2 - 3', '2' - 3'', 'minute 2 - 3', 'window 2 - 3', 'ventana 2 - 3'
        // Specification: "2 - 3 means 'from minute 2 to minute 3'"
        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(?:from\s+)?(?:minute|min|minuto|slice|window|periodo|period|ventana)\s+(\d{1,3})['’]?\s*[-–—]\s*(\d{1,3})['’]?(?:\s*(?:minute|minutes|minutos|mins))?\b",
            m =>
            {
                var m1 = m.Groups[1].Value;
                var m2 = m.Groups[2].Value;
                return isSpanish
                    ? $"del minuto {m1} al minuto {m2}"
                    : $"from minute {m1} to minute {m2}";
            });

        // 4. Any standalone spaced two-number range: "2 - 3" (user specification: "2 - 3 means 'from minute 2 to minute 3'")
        normalized = Regex.Replace(
            normalized,
            @"\b(\d{1,3})\s+[-–—]\s+(\d{1,3})\b",
            m =>
            {
                var m1 = m.Groups[1].Value;
                var m2 = m.Groups[2].Value;
                return isSpanish
                    ? $"del minuto {m1} al minuto {m2}"
                    : $"from minute {m1} to minute {m2}";
            });

        // 5. Convert match minute marks: 90' --> doesn't mean feet, read as '90 minutes' or 'minute 90'
        // A. Added time with apostrophe, e.g. 90+4' or 45+2'
        normalized = Regex.Replace(
            normalized,
            @"\b(\d{1,3})\+(\d{1,2})['’]",
            m => isSpanish
                ? $"minuto {m.Groups[1].Value} más {m.Groups[2].Value}"
                : $"minute {m.Groups[1].Value} plus {m.Groups[2].Value}");

        // B. Minute preceded by prepositions: 'at 90'', 'in the 90'', 'en el 90'', 'al 90''
        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(at|in|on|by|around)\s+(?:the\s+)?(\d{1,3})['’]",
            m => isSpanish
                ? $"{m.Groups[1].Value} el minuto {m.Groups[2].Value}"
                : $"{m.Groups[1].Value} minute {m.Groups[2].Value}");

        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(en|al|en el|minuto)\s+(\d{1,3})['’]",
            m => $"{m.Groups[1].Value} minuto {m.Groups[2].Value}");

        // C. Standalone minute with apostrophe: e.g. 90' -> 90 minutes
        // Specification: "90'--> doesn't mean feet. In this context it's read as 90 minutes."
        normalized = Regex.Replace(
            normalized,
            @"\b(\d{1,3})['’]",
            m => isSpanish
                ? $"minuto {m.Groups[1].Value}"
                : $"{m.Groups[1].Value} minutes");

        // 6. Restore protected scores
        for (int i = 0; i < scorePlaceholders.Count; i++)
        {
            normalized = normalized.Replace($"__SCORE_{i}__", scorePlaceholders[i]);
        }

        // 7. Restore protected formations
        for (int i = 0; i < formationPlaceholders.Count; i++)
        {
            normalized = normalized.Replace($"__FORMATION_{i}__", formationPlaceholders[i]);
        }

        return normalized;
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
