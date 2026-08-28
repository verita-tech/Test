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

**Es gibt bewusst weder An- noch Abmeldung in der Oberfläche.** Kein Login-Formular, kein
Abmelden-Knopf, keinen `/signout`-Endpunkt. Die Anmeldung passiert automatisch beim ersten Aufruf
(per Kerberos/SPNEGO ohne jede Eingabe), und eine abgelaufene Sitzung wird ebenso automatisch
erneuert. Eine Abmeldung würde den Benutzer nur unmittelbar in eine neue Sitzung zurückwerfen.

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
| Web origins | leer — die App läuft im selben Origin, CORS wird nicht gebraucht |

Eine Post-Logout-Redirect-URI wird nicht gebraucht, weil die Anwendung keine Abmeldung anbietet.

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
| `/api/me` | Cookie oder Bearer | `MeController` — Anzeigename, Berechtigungsstatus, Claims für den `AuthenticationStateProvider` |
| `/api/weatherforecast` | Cookie oder Bearer, Policy `AdAccess` | `WeatherForecastController` — Beispiel-API |
| `/api/<unbekannt>` | — | **404**, damit ein Tippfehler nicht als SPA-Shell zurückkommt |
| `/signin-oidc` | anonym | OIDC-Callback, schließt die Anmeldung ab |
| `/signout-oidc` | anonym | Front-Channel-Logout, wenn **Keycloak** die SSO-Sitzung beendet; der nächste Aufruf meldet den Benutzer still wieder an |

## Routing: Web API mit Controllern plus SPA-Fallback

`Test.Web.Api` ist eine ganz normale ASP.NET-Core-Web-API: Die gesamte API-Oberfläche liegt in
Controllern unter `Controllers/` (`MeController`, `WeatherForecastController`), registriert über
`AddControllers()`/`MapControllers()`. Nichts an der Keycloak-Anbindung hängt an Minimal APIs.

Dazu kommt `MapFallbackToFile("index.html")`. Das ist kein Notbehelf, sondern der vorgesehene
Mechanismus für clientseitiges Routing: Ein Deep Link wie `/weather` existiert serverseitig nicht,
der Browser muss trotzdem die App-Shell bekommen, damit der Blazor-Router die Route übernimmt. Ohne
Fallback liefert ein Reload auf `/weather` einen 404.

Drei Dinge begrenzen den Fallback, damit er nichts verschluckt:

- Er greift **nur nach** allen Controller-Routen — Endpunkte haben immer Vorrang.
- Das Standardmuster `{*path:nonfile}` schließt Pfade aus, die wie Dateien aussehen. Eine fehlende
  `.js` oder `.css` liefert also weiterhin einen echten 404 und nicht HTML mit Status 200.
- `app.Map("/api/{**path}", ...)` fängt unbekannte API-Pfade vorher ab und antwortet mit 404.
  Sonst bekäme ein vertippter Endpunkt die SPA-Shell zurück, und der Client scheiterte an einem
  JSON-Parse-Fehler statt an einem klaren 404.

`/api/*` antwortet unangemeldet mit **401**, nicht mit einem Redirect — nur so kann der
`UnauthorizedRedirectHandler` im Client eine abgelaufene Session erkennen und per vollem Reload eine
neue Anmeldung auslösen (bei Kerberos-SSO für den Benutzer unsichtbar).

## Hinweis zur Cookie-Größe

`SaveTokens` steht auf `false`, damit im Cookie nur Claims landen und es unter der 4-KB-Grenze
bleibt. Braucht das Backend später das Access-Token für nachgelagerte Aufrufe, `SaveTokens = true`
setzen **und** einen serverseitigen `ITicketStore` konfigurieren — sonst wird das Cookie gechunkt
(`.C1`, `.C2`, …).
