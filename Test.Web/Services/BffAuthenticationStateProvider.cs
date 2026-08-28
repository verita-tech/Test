using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Test.Web.Services;

/// <summary>
/// Builds the client-side authentication state from the backend session instead of from a token.
/// In the BFF setup the browser holds nothing but an HttpOnly cookie it cannot read, so the claims
/// are fetched from /api/me, which only answers for an authenticated session.
/// </summary>
public sealed class BffAuthenticationStateProvider(ICurrentUserService currentUserService)
    : AuthenticationStateProvider
{
    private const string AuthenticationType = "bff";
    private const string NameClaimType = "preferred_username";
    private const string RoleClaimType = "groups";

    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private AuthenticationState? _cached;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var me = await currentUserService.GetCurrentUserAsync();
        if (me.Claims.Count == 0)
        {
            // /api/me answered 401 or failed: treat as signed out and let the next navigation
            // run into the backend gate, which redirects to Keycloak.
            return Anonymous;
        }

        var identity = new ClaimsIdentity(
            me.Claims.Select(c => new Claim(c.Type, c.Value)),
            AuthenticationType,
            NameClaimType,
            RoleClaimType);

        _cached = new AuthenticationState(new ClaimsPrincipal(identity));
        return _cached;
    }

    /// <summary>Drops the cached state and re-reads it from the backend.</summary>
    public void Invalidate()
    {
        _cached = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
