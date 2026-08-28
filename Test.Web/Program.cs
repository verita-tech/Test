using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using Test.Web;
using Test.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();

// The app is served by Test.Web.Api and authenticates against it with an HttpOnly cookie the browser
// attaches automatically. No OIDC, no client id and no token handling live here: the backend is the
// confidential Keycloak client and the browser never sees a token.

// Transient on purpose: IHttpClientFactory rebuilds the handler chain when it expires and a
// DelegatingHandler instance cannot be reused across chains.
builder.Services.AddTransient<UnauthorizedRedirectHandler>();

builder.Services.AddHttpClient("Api", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<UnauthorizedRedirectHandler>();

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<BffAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<BffAuthenticationStateProvider>());

await builder.Build().RunAsync();
