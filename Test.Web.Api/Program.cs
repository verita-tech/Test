using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Test.Web.Api.Authorization;
using Test.Web.Api.Options;
using Test.Web.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.Configure<AdAccessOptions>(builder.Configuration.GetSection(AdAccessOptions.SectionName));
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.SectionName));

builder.Services.AddSingleton<IAdAccessChecker, AdAccessChecker>();
builder.Services.AddSingleton<IAuthorizationHandler, AdAccessHandler>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"];
        options.Audience = builder.Configuration["Keycloak:Audience"];
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("AdAccess", policy => policy.Requirements.Add(new AdAccessRequirement()));

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        // Auth is via the "Authorization: Bearer" header, not browser credentials, so
        // AllowCredentials() is not needed here.
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

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

app.UseCors("BlazorClient");

app.UseAuthentication();
app.UseAuthorization();

// Authenticated-only endpoint (no AdAccess policy) so the Blazor app can show
// "signed in as X but not authorized" instead of a bare 403 without context.
app.MapGet("/api/me", (ClaimsPrincipal user, IAdAccessChecker checker) =>
{
    var displayName = user.FindFirst("name")?.Value ?? user.FindFirst("preferred_username")?.Value ?? "Unbekannt";
    return Results.Ok(new MeResponse(displayName, checker.IsAuthorized(user)));
}).RequireAuthorization();

app.MapControllers();

app.Run();

public sealed record MeResponse(string DisplayName, bool IsAuthorized);
