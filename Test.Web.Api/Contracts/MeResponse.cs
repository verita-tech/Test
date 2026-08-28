namespace Test.Web.Api.Contracts;

public sealed record ClaimDto(string Type, string Value);

public sealed record MeResponse(string DisplayName, bool IsAuthorized, IReadOnlyList<ClaimDto> Claims);
