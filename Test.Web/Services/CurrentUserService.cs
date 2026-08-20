using System.Net.Http.Json;

namespace Test.Web.Services;

public sealed record MeResponse(string DisplayName, bool IsAuthorized);

public interface ICurrentUserService
{
    Task<MeResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}

public sealed class CurrentUserService(HttpClient httpClient) : ICurrentUserService
{
    public async Task<MeResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<MeResponse>("api/me", cancellationToken);
            return response ?? new MeResponse("Unbekannt", false);
        }
        catch (Exception)
        {
            return new MeResponse("Unbekannt", false);
        }
    }
}
