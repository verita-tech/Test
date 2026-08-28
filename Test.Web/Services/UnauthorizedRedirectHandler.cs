using System.Net;
using Microsoft.AspNetCore.Components;

namespace Test.Web.Services;

/// <summary>
/// The backend session can expire while the app is loaded. A 401 then means the cookie is gone, so
/// force a full page load: that request hits the backend's authentication gate, which redirects to
/// Keycloak (silently, when Kerberos SSO is configured) and brings the user back to the same place.
/// </summary>
public sealed class UnauthorizedRedirectHandler(NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            navigation.NavigateTo(navigation.Uri, forceLoad: true);
        }

        return response;
    }
}
