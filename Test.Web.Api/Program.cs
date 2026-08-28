using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Test.Web.Api.Authorization;
using Test.Web.Api.Middleware;
using Test.Web.Api.Options;
using Test.Web.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.Configure<AdAccessOptions>(builder.Configuration.GetSection(AdAccessOptions.SectionName));
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.SectionName));

builder.Services.AddSingleton<IAdAccessChecker, AdAccessChecker>();
builder.Services.AddSingleton<IAuthorizationHandler, AdAccessHandler>();

var keycloak = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
               ?? new KeycloakOptions();

// Backend-for-Frontend: the browser only ever holds an HttpOnly session cookie. This backend is a
// confidential Keycloak client and runs the authorization code flow server-side, authenticating the
// code-for-token exchange with its client credentials (ClientId + ClientSecret). Tokens never reach
// the browser. JWT bearer stays available in parallel for service-to-service callers.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-Test.Session";
        options.Cookie.HttpOnly = true;
        // The __Host- prefix is only honoured by browsers for Secure, Path=/ cookies without a Domain.
        options.Cookie.Path = "/";
        // Lax rather than Strict: the browser must send the cookie on the redirect back from Keycloak.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // RequireAuthenticatedUserMiddleware already redirects browser navigations to Keycloak, so
        // anything reaching these events is a programmatic call that wants a status code, not HTML.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = keycloak.Authority;
        options.ClientId = keycloak.ClientId;
        options.ClientSecret = keycloak.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Keep the cookie small: without the tokens it holds claims only and stays well under the
        // 4 KB limit that would otherwise force chunking. Enable SaveTokens together with a
        // server-side ITicketStore if this backend ever needs the access token for downstream calls.
        options.SaveTokens = false;

        // Preserve Keycloak's raw claim names (preferred_username, groups) so the cookie principal
        // and the bearer principal look identical to AdAccessChecker.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = keycloak.UpnClaimType;

        // Groups come from the ID token, where the token handler expands the JSON array into one
        // claim per group. Reading them from the UserInfo endpoint instead would need a custom
        // ClaimAction, because ClaimActions.MapJsonKey only maps the first element of an array.
        options.GetClaimsFromUserInfoEndpoint = false;

        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
    })
    .AddJwtBearer(options =>
    {
        options.Authority = keycloak.Authority;
        options.Audience = keycloak.Audience;
        options.MapInboundClaims = false;
    });

// Both schemes are accepted everywhere: the Blazor app authenticates by cookie, service callers by
// bearer token, and AdAccessChecker sees the same claim shape either way.
string[] authenticationSchemes =
[
    CookieAuthenticationDefaults.AuthenticationScheme,
    JwtBearerDefaults.AuthenticationScheme
];

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder(authenticationSchemes)
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("AdAccess", policy => policy
        .AddAuthenticationSchemes(authenticationSchemes)
        .RequireAuthenticatedUser()
        .AddRequirements(new AdAccessRequirement()));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();

// Must sit before the static file middleware: static files bypass authorization entirely, so this
// is what keeps index.html and /_framework/* from being served to anonymous callers.
app.UseMiddleware<RequireAuthenticatedUserMiddleware>();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthorization();

// Authenticated-only endpoint (no AdAccess policy) so the Blazor app can show
// "signed in as X but not authorized" instead of a bare 403 without context.
// The claims it returns are what the client's AuthenticationStateProvider builds its principal from.
app.MapGet("/api/me", (ClaimsPrincipal user, IAdAccessChecker checker) =>
{
    var displayName = user.FindFirst("name")?.Value ?? user.FindFirst("preferred_username")?.Value ?? "Unbekannt";

    string[] exposedClaimTypes = ["name", "preferred_username", "email", "groups"];
    var claims = user.Claims
        .Where(c => exposedClaimTypes.Contains(c.Type))
        .Select(c => new ClaimDto(c.Type, c.Value))
        .ToArray();

    return Results.Ok(new MeResponse(displayName, checker.IsAuthorized(user), claims));
}).RequireAuthorization();

// Signs out locally and at Keycloak (RP-initiated logout), then lands back on the app, which
// triggers a fresh login.
app.MapGet("/signout", () => Results.SignOut(
    new AuthenticationProperties { RedirectUri = "/" },
    new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme }))
    .AllowAnonymous();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

public sealed record ClaimDto(string Type, string Value);
public sealed record MeResponse(string DisplayName, bool IsAuthorized, IReadOnlyList<ClaimDto> Claims);
