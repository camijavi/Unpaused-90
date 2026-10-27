using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace NETUnpaused90.Services;

/// <summary>
/// Singleton TokenCredential that manages Microsoft Entra ID access tokens per scope:
/// - In Production: DefaultAzureCredential automatically leverages Azure Managed Identity without configuration.
/// - In Local Development: Leverages Azure CLI, Visual Studio, VS Code, and interactive browser login.
/// - Tokens are securely cached per scope in memory and on disk (unexpired only) to prevent redundant network calls.
/// </summary>
public class AppTokenCredential : TokenCredential
{
    private readonly AzureCliCredential _cliCredential;
    private readonly DefaultAzureCredential _defaultAzureCredential;
    private readonly SharedTokenCacheCredential _sharedCacheCredential;
    private readonly InteractiveBrowserCredential _browserCredential;
    private readonly ILogger<AppTokenCredential> _logger;
    private readonly string _diskCachePath;
    private readonly string? _tenantId;
    private readonly ConcurrentDictionary<string, AccessToken> _tokenCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _scopeLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _scopeCoolDown = new(StringComparer.OrdinalIgnoreCase);

    private record CachedTokenDto(string Token, DateTimeOffset ExpiresOn);

    public AppTokenCredential(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<AppTokenCredential> logger)
    {
        _logger = logger;
        _tenantId = configuration["AzureBlob:TenantId"];

        EnsureAzureCliInPath();

        var cacheDir = Path.Combine(env.ContentRootPath, "obj");
        Directory.CreateDirectory(cacheDir);
        _diskCachePath = Path.Combine(cacheDir, "entra-token-cache.json");

        LoadTokensFromDisk();

        var persistenceOptions = new TokenCachePersistenceOptions
        {
            Name = "Unpaused90EntraTokenCache",
            UnsafeAllowUnencryptedStorage = true
        };

        _cliCredential = new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId = _tenantId
        });

        _sharedCacheCredential = new SharedTokenCacheCredential(new SharedTokenCacheCredentialOptions
        {
            TenantId = _tenantId,
            TokenCachePersistenceOptions = persistenceOptions
        });

        _defaultAzureCredential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId = _tenantId,
            Diagnostics = { IsLoggingEnabled = true }
        });

        _browserCredential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = _tenantId,
            TokenCachePersistenceOptions = persistenceOptions
        });
    }

    private static void EnsureAzureCliInPath()
    {
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] candidateDirs = [
            @"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin",
            @"C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin"
        ];

        var toAdd = candidateDirs
            .Where(dir => Directory.Exists(dir) && !currentPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toAdd.Count > 0)
        {
            Environment.SetEnvironmentVariable("PATH", string.Join(";", toAdd) + ";" + currentPath);
        }
    }

    private static string? FindAzureCliPath()
    {
        string[] knownPaths = [
            @"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd",
            @"C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
        ];
        foreach (var path in knownPaths)
        {
            if (File.Exists(path)) return path;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "az.cmd");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();
    }

    public override async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var scopeKey = string.Join(" ", requestContext.Scopes);

        // 1. Check in-memory token cache (valid if > 2 minutes remaining)
        if (_tokenCache.TryGetValue(scopeKey, out var cached) &&
            cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return cached;
        }

        // Avoid hammering Entra ID if this scope failed recently
        if (_scopeCoolDown.TryGetValue(scopeKey, out var coolDownUntil) && DateTimeOffset.UtcNow < coolDownUntil)
        {
            if (_tokenCache.TryGetValue(scopeKey, out var unexpired) && unexpired.ExpiresOn > DateTimeOffset.UtcNow)
            {
                return unexpired;
            }
            throw new InvalidOperationException($"Entra ID token acquisition in cooldown for scope '{scopeKey}'. Please authenticate via Azure CLI (az login) or Azure Managed Identity.");
        }

        var scopeLock = _scopeLocks.GetOrAdd(scopeKey, _ => new SemaphoreSlim(1, 1));
        await scopeLock.WaitAsync(cancellationToken);
        try
        {
            if (_tokenCache.TryGetValue(scopeKey, out cached) &&
                cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached;
            }

            // 2. Try Azure CLI Credential (fastest for local dev)
            try
            {
                _logger.LogInformation("Requesting Entra ID token for scope '{Scope}' via Azure CLI...", scopeKey);
                using var cliCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cliCts.Token);
                var cliToken = await _cliCredential.GetTokenAsync(requestContext, linkedCts.Token);
                if (!string.IsNullOrWhiteSpace(cliToken.Token) && cliToken.ExpiresOn > DateTimeOffset.UtcNow)
                {
                    _tokenCache[scopeKey] = cliToken;
                    SaveTokensToDisk();
                    _logger.LogInformation("Successfully acquired Entra ID token for scope '{Scope}' via Azure CLI.", scopeKey);
                    return cliToken;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Azure CLI credential check bypassed: {Message}", ex.Message);
            }

            // 3. Fallback to direct az.cmd execution (if AzureCliCredential had PATH or child-process quirks)
            try
            {
                var directToken = await TryGetTokenViaDirectAzCliAsync(requestContext, cancellationToken);
                if (directToken is not null && directToken.Value.ExpiresOn > DateTimeOffset.UtcNow)
                {
                    _tokenCache[scopeKey] = directToken.Value;
                    SaveTokensToDisk();
                    _logger.LogInformation("Successfully acquired Entra ID token for scope '{Scope}' via direct Azure CLI runner.", scopeKey);
                    return directToken.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Direct Azure CLI runner check bypassed: {Message}", ex.Message);
            }

            // 4. Try DefaultAzureCredential (Managed Identity in Production, VS / VS Code / Environment in Dev)
            try
            {
                _logger.LogInformation("Requesting Entra ID token for scope '{Scope}' via DefaultAzureCredential...", scopeKey);
                using var defaultCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, defaultCts.Token);
                var defaultToken = await _defaultAzureCredential.GetTokenAsync(requestContext, linkedCts.Token);
                if (!string.IsNullOrWhiteSpace(defaultToken.Token) && defaultToken.ExpiresOn > DateTimeOffset.UtcNow)
                {
                    _tokenCache[scopeKey] = defaultToken;
                    SaveTokensToDisk();
                    _logger.LogInformation("Successfully acquired Entra ID token for scope '{Scope}' via DefaultAzureCredential.", scopeKey);
                    return defaultToken;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("DefaultAzureCredential could not acquire token for scope '{Scope}': {Message}", scopeKey, ex.Message);
            }

            // 5. Try silent MSAL cache
            try
            {
                using var silentCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, silentCts.Token);
                var silentToken = await _sharedCacheCredential.GetTokenAsync(requestContext, linkedCts.Token);
                if (!string.IsNullOrWhiteSpace(silentToken.Token) && silentToken.ExpiresOn > DateTimeOffset.UtcNow)
                {
                    _tokenCache[scopeKey] = silentToken;
                    SaveTokensToDisk();
                    _logger.LogInformation("Silently acquired Entra ID token for scope '{Scope}' from MSAL cache.", scopeKey);
                    return silentToken;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Silent MSAL token check bypassed: {Message}", ex.Message);
            }

            // 6. Try Interactive Browser Credential if in an interactive environment
            if (Environment.UserInteractive)
            {
                try
                {
                    _logger.LogInformation("Prompting interactive browser login for Entra ID scope '{Scope}'...", scopeKey);
                    using var browserCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, browserCts.Token);
                    var browserToken = await _browserCredential.GetTokenAsync(requestContext, linkedCts.Token);
                    if (!string.IsNullOrWhiteSpace(browserToken.Token) && browserToken.ExpiresOn > DateTimeOffset.UtcNow)
                    {
                        _tokenCache[scopeKey] = browserToken;
                        SaveTokensToDisk();
                        _logger.LogInformation("Successfully authenticated interactively for scope '{Scope}'.", scopeKey);
                        return browserToken;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Interactive token acquisition failed or skipped: {Message}", ex.Message);
                }
            }

            _scopeCoolDown[scopeKey] = DateTimeOffset.UtcNow.AddMinutes(1);

            // NEVER return an expired token!
            if (_tokenCache.TryGetValue(scopeKey, out var unexpiredToken) && unexpiredToken.ExpiresOn > DateTimeOffset.UtcNow)
            {
                return unexpiredToken;
            }

            throw new InvalidOperationException(
                $"Unable to acquire a valid Microsoft Entra ID token for scope '{scopeKey}'. " +
                "In Production: Ensure Managed Identity is enabled on your host and assigned the 'Cognitive Services OpenAI User' role on the Azure OpenAI resource. " +
                "In Local Development: Sign in via 'az login', Visual Studio / VS Code Azure Account extension, or set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables.");
        }
        finally
        {
            scopeLock.Release();
        }
    }

    private async Task<AccessToken?> TryGetTokenViaDirectAzCliAsync(TokenRequestContext requestContext, CancellationToken ct)
    {
        var cliPath = FindAzureCliPath();
        if (cliPath is null) return null;

        var scope = requestContext.Scopes.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(scope)) return null;

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = $"account get-access-token --scope \"{scope}\" -o json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

            await process.WaitForExitAsync(linked.Token);
            var stdout = await stdoutTask;

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                if (root.TryGetProperty("accessToken", out var tokenProp))
                {
                    var token = tokenProp.GetString();
                    DateTimeOffset expiresOn = DateTimeOffset.UtcNow.AddMinutes(50);
                    if (root.TryGetProperty("expires_on", out var expUnixProp) && expUnixProp.TryGetInt64(out var expUnix))
                    {
                        expiresOn = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                    }
                    else if (root.TryGetProperty("expiresOn", out var expStrProp) && DateTimeOffset.TryParse(expStrProp.GetString(), out var expParsed))
                    {
                        expiresOn = expParsed;
                    }

                    if (!string.IsNullOrWhiteSpace(token) && expiresOn > DateTimeOffset.UtcNow)
                    {
                        return new AccessToken(token, expiresOn);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Direct az.cmd execution skipped: {Message}", ex.Message);
        }

        return null;
    }

    private void LoadTokensFromDisk()
    {
        try
        {
            if (!File.Exists(_diskCachePath)) return;
            var json = File.ReadAllText(_diskCachePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, CachedTokenDto>>(json);
            if (dict is null) return;

            foreach (var kvp in dict)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value.Token) && kvp.Value.ExpiresOn > DateTimeOffset.UtcNow)
                {
                    _tokenCache[kvp.Key] = new AccessToken(kvp.Value.Token, kvp.Value.ExpiresOn);
                }
            }
        }
        catch
        {
            // Ignore corrupted cache file
        }
    }

    private void SaveTokensToDisk()
    {
        try
        {
            var dict = _tokenCache
                .Where(kvp => kvp.Value.ExpiresOn > DateTimeOffset.UtcNow)
                .ToDictionary(
                    k => k.Key,
                    v => new CachedTokenDto(v.Value.Token, v.Value.ExpiresOn));
            var json = JsonSerializer.Serialize(dict);
            File.WriteAllText(_diskCachePath, json);
        }
        catch
        {
            // Ignore disk write issues
        }
    }
}
