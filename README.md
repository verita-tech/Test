# Test — Blazor WebAssembly hinter Keycloak (BFF)

Die Web API (`Test.Web.Api`) liefert die Blazor-WebAssembly-App (`Test.Web`) mit aus und ist
gleichzeitig der **vertrauliche Keycloak-Client**. Der Browser bekommt ausschließlich ein
HttpOnly-Session-Cookie — Access- und Refresh-Token verlassen den Server nie.

```
Browser ──HttpOnly-Cookie──> Test.Web.Api ──code + client_secret──> Keycloak ──LDAP/Kerberos──> AD
              │                    │
              │                    └── liefert /index.html, /_framework/*, /css/* aus
              └── keine Tokens, kein Keycloak-Kontakt, kein CORS
```

**Man muss immer authentifiziert sein.** `RequireAuthenticatedUserMiddleware` läuft vor dem
Static-File-Handling, weil Static Files keine Autorisierung ausführen. Ein unangemeldeter Aufruf
wird zu Keycloak umgeleitet, *bevor* `index.html` oder `/_framework/*` ausgeliefert werden.

Parallel akzeptiert die API weiterhin **JWT-Bearer-Token** für Dienst-zu-Dienst-Aufrufe. Beide
Pfade landen in derselben `AdAccess`-Policy (`AdAccessChecker`), die AD-Gruppen und UPNs prüft.

## Starten

```bash
dotnet user-secrets set "Keycloak:ClientSecret" "<secret>" --project Test.Web.Api
dotnet run --project Test.Web.Api --launch-profile https
```

Danach `https://localhost:7247`. `Test.Web` wird **nicht** mehr eigenständig gestartet — es hat
kein Startprofil mehr und wird vom API-Projekt referenziert und mit ausgeliefert.

> **HTTPS ist Pflicht.** Das Session-Cookie nutzt das `__Host-`-Präfix und `SecurePolicy.Always`.
> Über reines HTTP wird es nicht gesetzt, was zu einer Redirect-Schleife führt.

In Produktion kommt das Secret aus der Umgebungsvariablen `Keycloak__ClientSecret`. Es gehört in
**keine** eingecheckte Datei — insbesondere nicht nach `Test.Web/wwwroot/`, denn diese Dateien lädt
jeder Browser herunter.

## Konfiguration

`Test.Web.Api/appsettings.json`:

| Schlüssel | Bedeutung |
|---|---|
| `Keycloak:Authority` | Realm-URL, z. B. `https://keycloak.contoso.com/realms/contoso` |
| `Keycloak:ClientId` | vertraulicher Client dieses Backends, z. B. `test-bff` |
| `Keycloak:ClientSecret` | **nur** aus user-secrets / Umgebungsvariable |
| `Keycloak:Audience` | erwartete Audience auf dem Bearer-Pfad, z. B. `test-api` |
| `Keycloak:UpnClaimType` | Claim mit dem AD-UPN, Standard `preferred_username` |
| `Authorization:AllowedGroups` | zugelassene AD-Gruppen (Keycloak-Gruppenpfade, führender `/` optional) |
| `Authorization:AllowedUpns` | einzeln zugelassene Benutzer |

## Keycloak einrichten

### Client `test-bff`

| Einstellung | Wert |
|---|---|
| Client authentication | **ON** (vertraulich — hier entstehen die Client-Credentials) |
| Standard flow | ON |
| Direct access grants | OFF |
| Valid redirect URIs | `https://<host>/signin-oidc` |
| Valid post logout redirect URIs | `https://<host>/signout-callback-oidc` |
| Web origins | leer — die App läuft im selben Origin, CORS wird nicht gebraucht |

Für lokale Entwicklung `<host>` = `localhost:7247`.

### Mapper

- **Group Membership** → Token Claim Name `groups`, **Add to ID token: ON**, Add to access token: ON.
  Das ID-Token ist die Quelle für `AdAccessChecker`: der Token-Handler expandiert das JSON-Array
  automatisch zu einem Claim pro Gruppe. (Über den UserInfo-Endpoint ginge das nicht ohne eigene
  `ClaimAction` — `ClaimActions.MapJsonKey` überträgt bei Arrays nur den ersten Wert.)
- **Audience** → `test-api`, damit der parallele Bearer-Pfad die Audience prüfen kann.

### AD-Anbindung

LDAP User Federation auf den Unternehmens-DC einrichten, damit `preferred_username` den AD-UPN und
`groups` die AD-Gruppen liefert.

### Silent SSO per Kerberos/SPNEGO

Domänen-Rechner melden sich damit ohne Eingabemaske an.

1. Im LDAP-Provider **Kerberos-Integration aktivieren** (oder eine eigene Kerberos User Storage
   Federation anlegen).
2. Keytab für den SPN `HTTP/<keycloak-host>@CONTOSO.COM` erzeugen und in Keycloak hinterlegen;
   Kerberos-Realm und Server-Principal eintragen.
3. Browser konfigurieren:
   - **Edge/Chrome**: Gruppenrichtlinie `AuthServerAllowlist` = `<keycloak-host>` (bzw. Aufnahme in
     die Intranetzone).
   - **Firefox**: `network.negotiate-auth.trusted-uris` = `<keycloak-host>`.

> SPNEGO läuft zwischen **Browser und Keycloak**. Die API selbst braucht kein Negotiate und muss
> nicht domänengejoint sein.

## Endpunkte

| Pfad | Auth | Zweck |
|---|---|---|
| `/` und alle SPA-Routen | Cookie (erzwungen) | Blazor-App |
| `/api/me` | Cookie oder Bearer | Anzeigename, Berechtigungsstatus, Claims für den `AuthenticationStateProvider` |
| `/api/weatherforecast` | Cookie oder Bearer, Policy `AdAccess` | Beispiel-API |
| `/signout` | anonym | Abmeldung lokal **und** bei Keycloak (RP-initiated logout) |
| `/signin-oidc`, `/signout-callback-oidc` | anonym | OIDC-Callbacks |

`/api/*` antwortet unangemeldet mit **401**, nicht mit einem Redirect — nur so kann der
`UnauthorizedRedirectHandler` im Client eine abgelaufene Session erkennen und per vollem Reload eine
neue Anmeldung auslösen (bei Kerberos-SSO für den Benutzer unsichtbar).

## Hinweis zur Cookie-Größe

`SaveTokens` steht auf `false`, damit im Cookie nur Claims landen und es unter der 4-KB-Grenze
bleibt. Braucht das Backend später das Access-Token für nachgelagerte Aufrufe, `SaveTokens = true`
setzen **und** einen serverseitigen `ITicketStore` konfigurieren — sonst wird das Cookie gechunkt
(`.C1`, `.C2`, …).
