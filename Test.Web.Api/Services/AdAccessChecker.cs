using System.Security.Claims;
using Microsoft.Extensions.Options;
using Test.Web.Api.Options;

namespace Test.Web.Api.Services;

public sealed class AdAccessChecker(IOptionsMonitor<AdAccessOptions> options, IOptionsMonitor<KeycloakOptions> keycloakOptions) : IAdAccessChecker
{
    private const string GroupsClaimType = "groups";

    public bool IsAuthorized(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        var current = options.CurrentValue;

        var upn = principal.FindFirst(keycloakOptions.CurrentValue.UpnClaimType)?.Value;
        if (!string.IsNullOrEmpty(upn) &&
            current.AllowedUpns.Any(allowed => string.Equals(allowed, upn, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Keycloak's Group Membership mapper emits group paths like "/GroupName" or
        // "/Parent/Child" for nested groups; compare ignoring a leading slash on both sides.
        var groupNames = principal.FindAll(GroupsClaimType).Select(c => c.Value.TrimStart('/'));
        return groupNames.Any(group =>
            current.AllowedGroups.Any(allowed => string.Equals(allowed.TrimStart('/'), group, StringComparison.OrdinalIgnoreCase)));
    }
}
