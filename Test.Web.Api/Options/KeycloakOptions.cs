namespace Test.Web.Api.Options;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string Authority { get; init; } = "";
    public string Audience { get; init; } = "";

    /// <summary>Claim type that carries the user's AD UPN, depends on how the Keycloak AD/LDAP federation maps it.</summary>
    public string UpnClaimType { get; init; } = "preferred_username";
}
