using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace NETUnpaused90.Services;

/// <summary>
/// Shared Singleton TokenCredential that caches Entra ID access tokens per scope in memory AND on disk,
/// and leverages MSAL persistent token cache and silent credentials so access tokens are automatically
/// refreshed without blocking server requests or hanging in non-interactive background sessions.
/// </summary>
public class AppTokenCredential : TokenCredential
{
    private readonly SharedTokenCacheCredential _sharedCacheCredential;
    private readonly DefaultAzureCredential _defaultAzureCredential;
    private readonly AzureCliCredential _cliCredential;
    private readonly InteractiveBrowserCredential _browserCredential;
    private readonly ILogger<AppTokenCredential> _logger;
    private readonly string _diskCachePath;
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
        var tenantId = configuration["AzureBlob:TenantId"];

        var cacheDir = Path.Combine(env.ContentRootPath, "obj");
        Directory.CreateDirectory(cacheDir);
        _diskCachePath = Path.Combine(cacheDir, "entra-token-cache.json");

        LoadTokensFromDisk();

        var persistenceOptions = new TokenCachePersistenceOptions
        {
            Name = "Unpaused90EntraTokenCache",
            UnsafeAllowUnencryptedStorage = true
        };

        _sharedCacheCredential = new SharedTokenCacheCredential(new SharedTokenCacheCredentialOptions
        {
            TenantId = tenantId,
            TokenCachePersistenceOptions = persistenceOptions
        });

        _defaultAzureCredential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId = tenantId
        });

        _cliCredential = new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId = tenantId,
            ProcessTimeout = TimeSpan.FromSeconds(2)
        });

        _browserCredential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            TokenCachePersistenceOptions = persistenceOptions
        });
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

        // 1. Check in-memory token cache
        if (_tokenCache.TryGetValue(scopeKey, out var cached) &&
            cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return cached;
        }

        // Check if this scope failed recently (cooldown prevents repeated 5-10s hangs on expired tokens)
        if (_scopeCoolDown.TryGetValue(scopeKey, out var coolDownUntil) && DateTimeOffset.UtcNow < coolDownUntil)
        {
            if (_tokenCache.TryGetValue(scopeKey, out var stale) && !string.IsNullOrWhiteSpace(stale.Token))
            {
                return stale;
            }
            throw new InvalidOperationException($"Entra ID token acquisition in cooldown for scope '{scopeKey}'.");
        }

        // Use per-scope locks so token requests for different scopes never block each other
        var scopeLock = _scopeLocks.GetOrAdd(scopeKey, _ => new SemaphoreSlim(1, 1));
        await scopeLock.WaitAsync(cancellationToken);
        try
        {
            if (_tokenCache.TryGetValue(scopeKey, out cached) &&
                cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached;
            }

            if (_scopeCoolDown.TryGetValue(scopeKey, out coolDownUntil) && DateTimeOffset.UtcNow < coolDownUntil)
            {
                if (_tokenCache.TryGetValue(scopeKey, out var stale) && !string.IsNullOrWhiteSpace(stale.Token))
                {
                    return stale;
                }
                throw new InvalidOperationException($"Entra ID token acquisition in cooldown for scope '{scopeKey}'.");
            }

            // 2. Try SharedTokenCacheCredential (silently reuses persistent MSAL refresh token without browser prompts)
            try
            {
                using var silentCts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, silentCts.Token);
                var silentToken = await _sharedCacheCredential.GetTokenAsync(requestContext, linkedCts.Token);
                if (!string.IsNullOrWhiteSpace(silentToken.Token))
                {
                    _tokenCache[scopeKey] = silentToken;
                    SaveTokensToDisk();
                    _logger.LogInformation("Silently refreshed Entra ID token for scope '{Scope}' from MSAL cache.", scopeKey);
                    return silentToken;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Silent MSAL cache check bypassed ({Message}).", ex.Message);
            }

            // 3. Try DefaultAzureCredential (Managed Identity, Environment, VS/VSCode credentials)
            try
            {
                using var defaultCts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, defaultCts.Token);
                var defaultToken = await _defaultAzureCredential.GetTokenAsync(requestContext, linkedCts.Token);
                if (!string.IsNullOrWhiteSpace(defaultToken.Token))
                {
                    _tokenCache[scopeKey] = defaultToken;
                    SaveTokensToDisk();
                    return defaultToken;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("DefaultAzureCredential check bypassed ({Message}).", ex.Message);
            }

            // 4. Try Azure CLI quickly (1-second timeout)
            try
            {
                using var cliCts = new CancellationTokenSource(TimeSpan.FromSeconds(1.0));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cliCts.Token);
                var cliToken = await _cliCredential.GetTokenAsync(requestContext, linkedCts.Token);
                _tokenCache[scopeKey] = cliToken;
                SaveTokensToDisk();
                return cliToken;
            }
            catch
            {
                // Fall through
            }

            // 5. As a bounded fallback, try InteractiveBrowserCredential only if interactive with a 2-second timeout
            if (Environment.UserInteractive)
            {
                try
                {
                    using var browserCts = new CancellationTokenSource(TimeSpan.FromSeconds(2.0));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, browserCts.Token);
                    var browserToken = await _browserCredential.GetTokenAsync(requestContext, linkedCts.Token);
                    _tokenCache[scopeKey] = browserToken;
                    SaveTokensToDisk();
                    return browserToken;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Interactive token acquisition skipped ({Message}).", ex.Message);
                }
            }

            // 6. Set cooldown so we don't repeat the multi-second chain on every incoming request
            _scopeCoolDown[scopeKey] = DateTimeOffset.UtcNow.AddMinutes(2);

            // 7. Return existing cached token if available (even if close to expiration) rather than failing
            if (_tokenCache.TryGetValue(scopeKey, out var staleToken) && !string.IsNullOrWhiteSpace(staleToken.Token))
            {
                return staleToken;
            }

            throw new InvalidOperationException($"Unable to obtain Entra ID token for scope '{scopeKey}'.");
        }
        finally
        {
            scopeLock.Release();
        }
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
                if (!string.IsNullOrWhiteSpace(kvp.Value.Token))
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
            var dict = _tokenCache.ToDictionary(
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
