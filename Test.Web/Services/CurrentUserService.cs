using System.Net.Http.Json;

namespace Test.Web.Services;

public sealed record ClaimDto(string Type, string Value);

public sealed record MeResponse(string DisplayName, bool IsAuthorized, IReadOnlyList<ClaimDto> Claims)
{
    public static MeResponse Anonymous { get; } = new("Unbekannt", false, []);
}

public interface ICurrentUserService
{
    Task<MeResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}

public sealed class CurrentUserService(HttpClient httpClient) : ICurrentUserService
{
    // Both the authentication state provider and the layout ask for the current user during
    // startup; caching keeps that to a single request.
    private MeResponse? _cached;

    public async Task<MeResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            _cached = await httpClient.GetFromJsonAsync<MeResponse>("api/me", cancellationToken);
            return _cached ?? MeResponse.Anonymous;
        }
        catch (Exception)
        {
            return MeResponse.Anonymous;
        }
    }
}
