using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Net.Http.Headers;

namespace Test.Web.Api.Middleware;

/// <summary>
/// Enforces "you must always be authenticated" for everything the host serves, including the
/// Blazor WebAssembly files themselves. Static file middleware does not run authorization, so
/// without this gate index.html and /_framework/* would be downloadable anonymously.
/// Runs after UseAuthentication() and before UseBlazorFrameworkFiles()/UseStaticFiles().
/// </summary>
public sealed class RequireAuthenticatedUserMiddleware(RequestDelegate next)
{
    /// <summary>Endpoints that must stay reachable anonymously, otherwise the login itself deadlocks.</summary>
    private static readonly string[] AnonymousPaths =
    [
        "/signin-oidc",
        "/signout-oidc",
        "/signout-callback-oidc",
        "/signout",
        "/health"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity is { IsAuthenticated: true } || IsAnonymousPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        // UseAuthentication() only runs the default (cookie) scheme, so a bearer token has not been
        // validated at this point. Let those requests through and leave the decision to the endpoint's
        // authorization policy, which accepts the JWT bearer scheme as well.
        if (context.Request.Headers.ContainsKey(HeaderNames.Authorization))
        {
            await next(context);
            return;
        }

        // Anything under /api is called by code, not navigated to: answer with a status code it can
        // act on rather than a 302 to Keycloak's HTML login page.
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }

    private static bool IsAnonymousPath(PathString path) =>
        AnonymousPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
