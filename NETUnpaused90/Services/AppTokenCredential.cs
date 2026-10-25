using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace NETUnpaused90.Services;

/// <summary>
/// Shared Singleton TokenCredential that caches Entra ID access tokens per scope in memory AND on disk,
/// and enables MSAL TokenCachePersistenceOptions so a single sign-in silently refreshes across all scopes
/// (Blob Storage, Azure OpenAI, Azure Speech, and ARM) and survives server restarts.
/// </summary>
public class AppTokenCredential : TokenCredential
{
    private readonly AzureCliCredential _cliCredential;
    private readonly InteractiveBrowserCredential _browserCredential;
    private readonly TokenCredential? _prodCredential;
    private readonly ILogger<AppTokenCredential> _logger;
    private readonly string _diskCachePath;
    private readonly ConcurrentDictionary<string, AccessToken> _tokenCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _authLock = new(1, 1);

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

        _cliCredential = new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId = tenantId,
            ProcessTimeout = TimeSpan.FromSeconds(3)
        });

        _browserCredential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            TokenCachePersistenceOptions = persistenceOptions
        });

        if (!env.IsDevelopment())
        {
            _prodCredential = new DefaultAzureCredential();
        }
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

        if (_tokenCache.TryGetValue(scopeKey, out var cached) &&
            cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return cached;
        }

        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (_tokenCache.TryGetValue(scopeKey, out cached) &&
                cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached;
            }

            if (_prodCredential is not null)
            {
                var prodToken = await _prodCredential.GetTokenAsync(requestContext, cancellationToken);
                _tokenCache[scopeKey] = prodToken;
                SaveTokensToDisk();
                return prodToken;
            }

            // 1. Try Azure CLI quickly (3-second timeout so it never hangs)
            try
            {
                using var cliCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cliCts.CancelAfter(TimeSpan.FromSeconds(3));
                var cliToken = await _cliCredential.GetTokenAsync(requestContext, cliCts.Token);
                _tokenCache[scopeKey] = cliToken;
                SaveTokensToDisk();
                return cliToken;
            }
            catch
            {
                // Fall through to persistent InteractiveBrowserCredential
            }

            // 2. Use InteractiveBrowserCredential with persistent MSAL cache (reuses refresh token silently after 1st login)
            _logger.LogInformation("Acquiring Entra ID token for scope '{Scope}'...", scopeKey);
            var browserToken = await _browserCredential.GetTokenAsync(requestContext, cancellationToken);
            _tokenCache[scopeKey] = browserToken;
            SaveTokensToDisk();
            return browserToken;
        }
        finally
        {
            _authLock.Release();
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

            var now = DateTimeOffset.UtcNow.AddMinutes(2);
            foreach (var kvp in dict)
            {
                if (kvp.Value.ExpiresOn > now && !string.IsNullOrWhiteSpace(kvp.Value.Token))
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
