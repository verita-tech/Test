namespace Test.Web.Api.Options;

public sealed class AdAccessOptions
{
    public const string SectionName = "Authorization";

    public List<string> AllowedGroups { get; init; } = [];
    public List<string> AllowedUpns { get; init; } = [];
}
