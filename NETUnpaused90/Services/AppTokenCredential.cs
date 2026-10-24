using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;

namespace NETUnpaused90.Services;

/// <summary>
/// Shared Singleton TokenCredential that caches Entra ID access tokens per scope in memory
/// so MatchService, AiService, and SpeechService reuse tokens without opening extra browser tabs.
/// </summary>
public class AppTokenCredential : TokenCredential
{
    private readonly TokenCredential _innerCredential;
    private readonly ConcurrentDictionary<string, AccessToken> _tokenCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public AppTokenCredential(IConfiguration configuration, IWebHostEnvironment env)
    {
        var tenantId = configuration["AzureBlob:TenantId"];

        _innerCredential = env.IsDevelopment()
            ? new ChainedTokenCredential(
                new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),
                new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenantId }))
            : new DefaultAzureCredential();
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

            var token = await _innerCredential.GetTokenAsync(requestContext, cancellationToken);
            _tokenCache[scopeKey] = token;
            return token;
        }
        finally
        {
            _authLock.Release();
        }
    }
}
