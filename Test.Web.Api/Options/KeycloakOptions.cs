namespace Test.Web.Api.Options;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Realm URL, e.g. https://keycloak.contoso.com/realms/contoso.</summary>
    public string Authority { get; init; } = "";

    /// <summary>Confidential client used by this backend for the server-side authorization code flow.</summary>
    public string ClientId { get; init; } = "";

    /// <summary>Client secret. Comes from user-secrets (dev) or the Keycloak__ClientSecret env var (prod), never from a committed file.</summary>
    public string ClientSecret { get; init; } = "";

    /// <summary>Expected audience for tokens presented on the JWT bearer path.</summary>
    public string Audience { get; init; } = "";

    /// <summary>Claim type that carries the user's AD UPN, depends on how the Keycloak AD/LDAP federation maps it.</summary>
    public string UpnClaimType { get; init; } = "preferred_username";
}
