using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
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
        IWebHostEnvironment env,
        HttpClient httpClient)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;

        var tenantId = _configuration["AzureBlob:TenantId"];

        _credential = env.IsDevelopment()
            ? new ChainedTokenCredential(
                new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),
                new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenantId }))
            : new DefaultAzureCredential();
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

        // Default to en-US-AdamMultilingualNeural for both English and Spanish
        var voiceName = NormalizeVoiceName(request.Voice, defaultVoice);
        var isSpanish = string.Equals(request.Language?.Trim(), "es", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-ES", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.Language?.Trim(), "es-MX", StringComparison.OrdinalIgnoreCase);

        var xmlLang = isSpanish ? "es-MX" : "en-US";
        var escapedText = SecurityElement.Escape(request.Text);

        var ssml = $"""
            <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US">
              <voice name="{voiceName}">
                <lang xml:lang="{xmlLang}">
                  <prosody rate="+4%">
                    {escapedText}
                  </prosody>
                </lang>
              </voice>
            </speak>
            """;

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
            // Authenticate via Microsoft Entra ID using the aad#<resourceId>#<accessToken> bearer format
            var resourceId = await GetSpeechResourceIdAsync(cancellationToken);
            var tokenResult = await _credential.GetTokenAsync(
                new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                cancellationToken);

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
                $"Azure AI Speech returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}");
        }

        var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (audioBytes, voiceName, isSpanish ? "es" : "en");
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

        var armUrl = $"https://management.azure.com/resources?api-version=2021-04-01&$filter=name eq '{resourceName}'";
        using var armReq = new HttpRequestMessage(HttpMethod.Get, armUrl);
        armReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", armToken.Token);

        using var armRes = await _httpClient.SendAsync(armReq, cancellationToken);
        armRes.EnsureSuccessStatusCode();

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

        throw new InvalidOperationException(
            $"Could not auto-discover Azure Resource ID for Speech resource '{resourceName}'. " +
            "Add 'AzureSpeech:ResourceId' in appsettings.json.");
    }
}
