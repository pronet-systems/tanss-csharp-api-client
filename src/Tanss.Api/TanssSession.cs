using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tanss.Api.Rest;

namespace Tanss.Api;

/// <summary>
/// Die drei Token-Abläufe der Schnittstelle: anmelden, erneuern, für ein externes Programm prägen.
/// </summary>
/// <remarks>
/// <para>Die Sitzung schickt ihre Anfragen über denselben <see cref="HttpClient"/> wie der erzeugte
/// Client — mit derselben Basisadresse, derselben Zeitgrenze, demselben Proxy, derselben TLS-Einstellung,
/// derselben <c>loggedInUserId</c>-Regel und derselben Wiederholungsregel. Die Kopfzeilen setzt sie
/// allerdings selbst, weil jeder der drei Abläufe andere braucht: die Anmeldung die Zugangsdaten
/// (<c>user</c>/<c>password</c> oder <c>dbapikey</c>), die Erneuerung <c>refreshToken</c> und <b>kein</b>
/// <c>apiToken</c>, das Prägen das Sitzungstoken in <c>apiToken</c>.</para>
/// <para><b>Drei Anmeldewege, ein Filter.</b> Hinter <see cref="LoginPath"/> steht kein Controller,
/// sondern die Anmeldeprüfung des Servers. Sie nimmt <c>user</c>+<c>password</c>,
/// <c>user</c>+<c>logintoken</c> (beides <see cref="LoginAsync"/>) oder allein <c>dbapikey</c>
/// (<see cref="LoginWithDashboardKeyAsync"/>). Alle Zugangsdaten reisen in Kopfzeilen, und zwar in der
/// Kodierung, die <see cref="EncodeCredentialHeader"/> beschreibt.</para>
/// <para><b>Anmeldung und Erneuerung tragen nie das Token des Anbieters.</b> Die Anmeldung ist in der
/// Spezifikation mit <c>security: []</c> ausgewiesen, und die Erneuerung schlägt mit gesetztem
/// <c>apiToken</c> gemessen fehl (gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt
/// „Gemessen, nicht geraten“). Der <see cref="ITanssTokenProvider"/> wird für beide gar nicht erst
/// gefragt.</para>
/// <para><b>Die Anmeldung wird nicht wiederholt.</b> Sie ist ein <c>POST</c>, und
/// <see cref="TanssRestClientFactory.IsRepeatableRead"/> lässt nur <c>GET</c> in die Wiederholung nach
/// 429, 503 und 504.</para>
/// <para>Der erzeugte Client taugt für diese drei Abläufe nicht: Die 200-Antwort der Anmeldung hat in der
/// Spezifikation kein Schema, sondern nur ein Beispiel — es gäbe also nichts zu typisieren —, und den
/// gemessenen Weg <c>POST /api/v1/user/login</c> kennt die Spezifikation überhaupt nicht.</para>
/// </remarks>
public sealed class TanssSession
{
    /// <summary>
    /// Der gemessene Anmeldepfad: <c>POST /api/v1/user/login</c> mit den Zugangsdaten in Kopfzeilen
    /// (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“).
    /// </summary>
    /// <remarks>
    /// Auf einer 10.10-Instanz ist die Anmeldung <b>kein Controller</b>, sondern eine vorgeschaltete
    /// Prüfung auf <c>/api/v1/user/login</c>. Sie nimmt Zugangsdaten ausschließlich aus Kopfzeilen und
    /// akzeptiert <c>user</c>+<c>password</c>, <c>user</c>+<c>logintoken</c> oder <c>dbapikey</c>. Der
    /// dokumentierte Pfad <see cref="DocumentedLoginPath"/> antwortet dort mit 404.
    /// </remarks>
    public const string LoginPath = "/api/v1/user/login";

    /// <summary>
    /// Der Anmeldepfad, wie ihn die Spezifikation führt: <c>POST /api/v1/login</c> mit dem Rumpf
    /// <c>TnsLoginCredentials</c>.
    /// </summary>
    /// <remarks>
    /// Auf 10.10 gibt es ihn nicht — gemessen 404 (gegen TANSS 10.10 am 2026-09-15, siehe
    /// README, Abschnitt „Gemessen, nicht geraten“). <see cref="LoginAsync"/> weicht deshalb erst nach
    /// einer 404 auf <see cref="LoginPath"/> hierher aus; neuere Fassungen bedienen ihn.
    /// </remarks>
    public const string DocumentedLoginPath = "/api/v1/login";

    /// <summary>
    /// Die Kopfzeile mit dem Anmeldenamen (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README,
    /// Abschnitt „Gemessen, nicht geraten“).
    /// </summary>
    public const string UserHeader = "user";

    /// <summary>
    /// Die Kopfzeile mit dem Kennwort (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt
    /// „Gemessen, nicht geraten“).
    /// </summary>
    public const string PasswordHeader = "password";

    /// <summary>
    /// Die Kopfzeile mit dem anwendungsspezifischen Anmeldetoken; sie tritt an die Stelle von
    /// <see cref="PasswordHeader"/> (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt
    /// „Gemessen, nicht geraten“).
    /// </summary>
    public const string LoginTokenHeader = "logintoken";

    /// <summary>
    /// Die Kopfzeile des dritten Anmeldewegs: der Dashboard-Schlüssel, der Benutzername <b>und</b>
    /// Kennwort ersetzt (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen,
    /// nicht geraten“).
    /// </summary>
    /// <remarks>
    /// Trägt eine Anmeldeanfrage <b>nur</b> diese Kopfzeile, nimmt der Server ausschließlich diesen
    /// Zweig; <see cref="LoginWithDashboardKeyAsync"/> schickt genau das.
    /// </remarks>
    public const string DashboardKeyHeader = "dbapikey";

    /// <summary>
    /// Die Kopfzeile, in der das Erneuerungstoken reist (gemessen gegen TANSS 10.10 am 2026-09-15, siehe
    /// README, Abschnitt „Gemessen, nicht geraten“).
    /// </summary>
    /// <remarks>
    /// Die Schreibweise ist gleichgültig: Gemessen antworten <c>refreshToken</c>, <c>refreshtoken</c> und
    /// <c>RefreshToken</c> gleichermaßen mit 200 — HTTP-Kopfzeilen sind unabhängig von Groß- und
    /// Kleinschreibung. Geschickt wird die Schreibweise der Messung.
    /// </remarks>
    public const string RefreshTokenHeader = "refreshToken";

    /// <summary>
    /// Der Pfad, über den <see cref="RefreshAsync"/> erneuert: <c>GET /api/v1/employees/ownState</c>.
    /// </summary>
    /// <remarks>
    /// Erneuert wird laut Dokumentation über „every route apart from <c>/login</c> and
    /// <c>/user/login</c>“, und gemessen ist genau das: Das Erneuerungstoken in der Kopfzeile
    /// <see cref="RefreshTokenHeader"/> auf einer beliebigen Nicht-Anmelderoute liefert ein volles
    /// Anmeldeergebnis (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen,
    /// nicht geraten“). Gewählt ist eine kleine lesende Route mit der Rolle <c>USER</c>, die sowohl
    /// dokumentiert ist als auch auf 10.10 wirklich existiert (Token-Klasse <c>general</c>, Rolle
    /// <c>USER</c>, auf 10.10 vorhanden — siehe <see cref="ApiAvailability"/>) und auf dem Server nichts
    /// hinterlässt.
    /// </remarks>
    public const string RefreshPath = "/api/v1/employees/ownState";

    /// <summary>Der Pfad des Prägens ohne den Programmnamen: <c>/api/v1/jwts/{ext_program}</c>.</summary>
    public const string MintPath = "/api/v1/jwts";

    private readonly HttpClient _http;
    private readonly Uri _baseAddress;
    private readonly ITanssTokenProvider? _tokens;
    private readonly ILogger? _logger;

    /// <summary>Baut die Sitzung auf einer fertigen Verbindungsschicht.</summary>
    /// <param name="http">
    /// Der Client, den <see cref="TanssRestClientFactory.CreateHttpClient"/> gebaut hat: mit
    /// Basisadresse (auf <c>/</c> endend), Zeitgrenze, Proxy und der Handler-Kette.
    ///</param>
    /// <param name="tokens">
    /// Der Token-Anbieter. Ein <see cref="MutableTokenProvider"/> bekommt nach Anmeldung und
    /// Erneuerung den neuen <c>apiKey</c> eingetragen; jeder andere Anbieter bleibt unberührt.
    /// Gefragt wird er nur beim Prägen.
    ///</param>
    /// <param name="logger">Wahlweise; meldet Anmeldung, Erneuerung und Prägen auf <c>Information</c>.</param>
    /// <exception cref="ArgumentException">Der Client hat keine Basisadresse.</exception>
    public TanssSession(HttpClient http, ITanssTokenProvider? tokens = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _baseAddress = http.BaseAddress ?? throw new ArgumentException(
            "Der HttpClient der Sitzung braucht eine Basisadresse; TanssRestClientFactory.CreateHttpClient setzt sie.",
            nameof(http));
        _http = http;
        _tokens = tokens;
        _logger = logger;
    }

    /// <summary>
    /// Meldet einen Benutzer an und liefert das Paar aus Sitzungs- und Erneuerungstoken.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Weg ist gemessen</b> (gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt
    /// „Gemessen, nicht geraten“): <c>POST</c> auf <see cref="LoginPath"/> <b>ohne Rumpf</b>, die
    /// Zugangsdaten in den Kopfzeilen <see cref="UserHeader"/> und <see cref="PasswordHeader"/>. Ist
    /// <paramref name="loginToken"/> gesetzt, tritt <see cref="LoginTokenHeader"/> an die Stelle des
    /// Kennworts: Der Server nimmt <c>user</c>+<c>password</c>, <c>user</c>+<c>logintoken</c> oder
    /// <c>dbapikey</c> — sonst nichts. Ein JSON-Rumpf ohne Kopfzeilen führt gemessen zu HTTP 200 mit
    /// leerem Inhalt.</para>
    /// <para><b>Ausweichweg.</b> Antwortet <see cref="LoginPath"/> mit 404 — auf 10.10 tut er das nicht,
    /// auf einer künftigen Fassung womöglich —, wiederholt diese Methode die Anmeldung auf dem
    /// dokumentierten <see cref="DocumentedLoginPath"/> mit dem Rumpf
    /// <c>components.schemas.TnsLoginCredentials</c> (genau drei Felder: <c>username</c>,
    /// <c>password</c>, <c>token</c>). Welcher Weg getragen hat, steht danach in
    /// <see cref="TanssLoginResult.LoginPath"/>.</para>
    /// <para><b>Erfolg erkennt man nicht am Statuscode</b> (gemessen gegen TANSS 10.10 am 2026-09-15,
    /// siehe README, Abschnitt „Gemessen, nicht geraten“): Eine falsche Zugangsangabe antwortet mit HTTP
    /// 200 und <c>content.detailMessage</c> = <c>LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD</c>, eine
    /// unvollständige Anfrage mit HTTP 200 und leerem Rumpf. Maßgeblich ist <c>content.apiKey</c>; fehlt
    /// es, wirft <see cref="TanssLoginResult.Parse"/> eine <see cref="TanssApiException"/> mit diesem
    /// Code beziehungsweise mit <see cref="TanssLoginResult.EmptyResponseCode"/>.</para>
    /// <para><b>Ohne <c>apiToken</c>.</b> Die Operation trägt in der Spezifikation <c>security: []</c>;
    /// der Token-Anbieter wird hier nicht gefragt. War ein <see cref="MutableTokenProvider"/> übergeben,
    /// steht danach <see cref="TanssLoginResult.ApiKey"/> darin, und alle Clients daran arbeiten ohne
    /// Neubau weiter.</para>
    /// <para>Die Antwort trägt zwei Token: <c>content.apiKey</c> (vier Stunden) und
    /// <c>content.refresh</c> (fünf Tage), beide mit dem Präfix <c>Bearer </c>. Zusätzlich gilt laut
    /// Dokumentation „a 2-minute idle timeout“.</para>
    /// <para><b>Umlaute.</b> Name, Kennwort und Anmeldetoken gehen nicht wörtlich hinaus, sondern in der
    /// Kodierung, die der Server erwartet — siehe <see cref="EncodeCredentialHeader"/> (gemessen gegen
    /// TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“). Der dokumentierte
    /// Ausweichweg schickt sie unverändert, denn sein Rumpf ist JSON und damit ohnehin UTF-8.</para>
    /// </remarks>
    /// <param name="username">Der Anmeldename; geht als Kopfzeile <c>user</c> hinaus.</param>
    /// <param name="password">Das Kennwort; geht als Kopfzeile <c>password</c> hinaus. Ist
    /// <paramref name="loginToken"/> gesetzt, bleibt diese Kopfzeile weg, und der Wert wird nur noch für
    /// den dokumentierten Ausweichweg gebraucht.</param>
    /// <param name="loginToken">Das anwendungsspezifische Anmeldetoken (zweiter Faktor) für die
    /// Kopfzeile <c>logintoken</c>; <c>null</c>, wenn mit Kennwort angemeldet wird.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <exception cref="TanssApiException">Die Antwort trug kein <c>content.apiKey</c> (abgelehnte oder
    /// unvollständige Anmeldung), oder TANSS hat mit einem Fehlerstatus geantwortet.</exception>
    /// <exception cref="ArgumentException">Eine der Angaben trägt ein Steuerzeichen; in einer
    /// HTTP-Kopfzeile ist das nicht übertragbar (<see cref="EncodeCredentialHeader"/>).</exception>
    public async Task<TanssLoginResult> LoginAsync(string username, string password,
                                                   string? loginToken = null,
                                                   CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        string path = LoginPath;
        (HttpStatusCode status, string body) = await SendAsync(HeaderLogin(username, password, loginToken),
                                                               cancellationToken).ConfigureAwait(false);

        if (status == HttpStatusCode.NotFound)
        {
            // Der gemessene Weg fehlt: dann die dokumentierte Route mit dem JSON-Rumpf.
            path = DocumentedLoginPath;
            (status, body) = await SendAsync(DocumentedLogin(username, password, loginToken),
                                             cancellationToken).ConfigureAwait(false);
        }

        if (status is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw TanssApiException.FromResponse(status, body);
        }

        TanssLoginResult result = TanssLoginResult.Parse(body) with { LoginPath = path };
        Store(result);
        if (_logger?.IsEnabled(LogLevel.Information) == true)
        {
            _logger.LogInformation("Anmeldung über {Path} erfolgreich: Mitarbeiter {EmployeeId}, Token gültig bis {ExpiresAt:u}.",
                                   path, result.EmployeeId, result.ExpiresAt);
        }

        return result;
    }

    /// <summary>
    /// Meldet eine Dashboard-Anzeige an — der dritte Anmeldeweg, allein über die Kopfzeile
    /// <see cref="DashboardKeyHeader"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Vom Server so umgesetzt, gegen TANSS 10.10 geprüft</b> (siehe README, Abschnitt
    /// „Gemessen, nicht geraten“): Trägt die Anfrage <b>nur</b> die Kopfzeile <c>dbapikey</c> — kein
    /// <c>user</c>, kein <c>password</c> —, nimmt der Server ausschließlich diesen Zweig: Er sucht den
    /// einen aktiven Mitarbeiter, dessen Feld <c>TnsEmployee.dashboardApiKey</c> dem übergebenen Wert
    /// gleicht, prüft, dass dieser Mitarbeiter zur eigenen Firma gehört, setzt <c>dashboardLoginUsed =
    /// true</c> und gibt ein gewöhnliches Zugangstoken aus.</para>
    /// <para><b>Was dabei herauskommt</b>, ist ein Token vom Typ <c>ACCESS</c> wie nach
    /// <see cref="LoginAsync"/> — mit dem einen Unterschied, dass der Server darin den Anspruch
    /// <c>dashboardLogin</c> = <c>true</c> sieht. Das Ergebnis ist dasselbe
    /// <see cref="TanssLoginResult"/>: Sitzungs- und Erneuerungstoken mit dem Präfix <c>Bearer </c>,
    /// Mitarbeiter-Id und Ablauf. Erneuert wird danach wie sonst auch über
    /// <see cref="RefreshAsync"/>.</para>
    /// <para><b>Woher der Schlüssel kommt.</b> Er hängt am einzelnen Mitarbeiter
    /// (<c>TnsEmployee.dashboardApiKey</c>) und wird in der TANSS-Verwaltung gepflegt. Die Schnittstelle
    /// gibt ihn nicht heraus — gemessen trägt die Antwort von <c>GET /api/v1/employees/{id}</c> dieses
    /// Feld nicht. Wer ihn braucht, holt ihn also aus der Verwaltung, nicht über die API.</para>
    /// <para><b>Fehlschläge kommen wie jede abgelehnte Anmeldung als HTTP 200</b> mit <c>meta.text</c> =
    /// <c>Unsuccesful login attempt</c> und dem Code in <c>content.detailMessage</c>; er steht danach in
    /// <see cref="TanssApiException.ErrorCode"/>. Drei Codes gehören zu diesem Weg — alle drei führt die
    /// Aufzählung <c>TnsErrorCode</c>:</para>
    /// <list type="bullet">
    /// <item><description><c>LOGIN_ERROR_NO_USER_FOR_DASHBOARD</c> — kein aktiver Mitarbeiter trägt
    /// diesen Schlüssel. <b>Live gemessen</b> am 2026-09-15 mit einem erfundenen
    /// Schlüssel.</description></item>
    /// <item><description><c>LOGIN_ERROR_TOO_MANY_USER_FOR_DASHBOARD</c> — mehr als ein Mitarbeiter trägt
    /// ihn; der Anbieter verlangt genau einen.</description></item>
    /// <item><description><c>LOGIN_ERROR_DASHBOARD_USER_NOT_WITHIN_OWN_COMPANY</c> — der Treffer gehört
    /// nicht zur eigenen Firma.</description></item>
    /// </list>
    /// <para><b>Ausweichweg.</b> Antwortet <see cref="LoginPath"/> mit 404 — auf 10.10 tut er das nicht
    /// —, wiederholt diese Methode die Anmeldung auf dem dokumentierten <see cref="DocumentedLoginPath"/>
    /// mit dem Rumpf <c>{"dbapikey":"…"}</c>: Der Server nimmt dort neben <c>username</c>,
    /// <c>password</c> und <c>token</c> auch <c>dbapikey</c> entgegen — anders als die offizielle
    /// Schnittstellenbeschreibung, die nur die ersten drei kennt. Welcher Weg getragen hat, steht danach
    /// in <see cref="TanssLoginResult.LoginPath"/>.</para>
    /// <para><b>Ohne <c>apiToken</c>.</b> Wie jede Anmeldung trägt auch diese kein Token des Anbieters;
    /// ein <see cref="MutableTokenProvider"/> bekommt danach den neuen <c>apiKey</c> eingetragen.</para>
    /// </remarks>
    /// <param name="dashboardApiKey">Der Schlüssel aus <c>TnsEmployee.dashboardApiKey</c>; geht —
    /// kodiert wie in <see cref="EncodeCredentialHeader"/> beschrieben — als Kopfzeile <c>dbapikey</c>
    /// hinaus.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <exception cref="TanssApiException">Die Antwort trug kein <c>content.apiKey</c> — bei diesem Weg
    /// mit einem der drei <c>LOGIN_ERROR_…_DASHBOARD…</c>-Codes —, oder TANSS hat mit einem Fehlerstatus
    /// geantwortet.</exception>
    /// <exception cref="ArgumentException">Der Schlüssel ist leer oder trägt ein Steuerzeichen
    /// (<see cref="EncodeCredentialHeader"/>).</exception>
    public async Task<TanssLoginResult> LoginWithDashboardKeyAsync(string dashboardApiKey,
                                                                   CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardApiKey);

        string path = LoginPath;
        (HttpStatusCode status, string body) = await SendAsync(HeaderDashboardLogin(dashboardApiKey),
                                                               cancellationToken).ConfigureAwait(false);

        if (status == HttpStatusCode.NotFound)
        {
            // Der gemessene Weg fehlt: dann die dokumentierte Route mit dem Feld dbapikey.
            path = DocumentedLoginPath;
            (status, body) = await SendAsync(DocumentedDashboardLogin(dashboardApiKey), cancellationToken)
                .ConfigureAwait(false);
        }

        if (status is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw TanssApiException.FromResponse(status, body);
        }

        TanssLoginResult result = TanssLoginResult.Parse(body) with { LoginPath = path };
        Store(result);
        if (_logger?.IsEnabled(LogLevel.Information) == true)
        {
            _logger.LogInformation("Dashboard-Anmeldung über {Path} erfolgreich: Mitarbeiter {EmployeeId}, Token gültig bis {ExpiresAt:u}.",
                                   path, result.EmployeeId, result.ExpiresAt);
        }

        return result;
    }

    /// <summary>
    /// Tauscht das Erneuerungstoken gegen ein frisches Paar — über die Kopfzeile
    /// <see cref="RefreshTokenHeader"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Gemessen gegen TANSS 10.10 am 2026-09-15</b> (siehe README, Abschnitt „Gemessen, nicht
    /// geraten“): Das Erneuerungstoken in der Kopfzeile <c>apiToken</c> antwortet <b>403</b>. Dasselbe
    /// Token in der Kopfzeile <c>refreshToken</c>, und zwar <b>ohne</b> jede Kopfzeile <c>apiToken</c>,
    /// antwortet 200 mit einem vollständigen Anmeldeergebnis — neues <c>apiKey</c> und neues
    /// <c>refresh</c>. Genau so schickt diese Methode die Anfrage: ein <c>GET</c> auf
    /// <paramref name="path"/>, <c>refreshToken</c> gesetzt, <c>apiToken</c> weggelassen. Die eigentliche
    /// Nutzlast der Route wird verworfen.</para>
    /// <para>Die Schreibweise der Kopfzeile ist gleichgültig (<c>refreshToken</c>, <c>refreshtoken</c>,
    /// <c>RefreshToken</c> — alle gemessen mit 200); HTTP-Kopfzeilen sind unabhängig von Groß- und
    /// Kleinschreibung.</para>
    /// <para><b>Einen Erneuerungspfad gibt es nicht.</b> Die offizielle Schnittstellenbeschreibung
    /// erwähnt im Fließtext „or call <c>POST /api/v1/login/refresh</c> with the refresh token“. Auf 10.10
    /// existiert dieser Pfad nicht (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt
    /// „Gemessen, nicht geraten“), und er steht weder in der offiziellen Schnittstellenbeschreibung noch
    /// unter den Routen, die 10.10 bedient. Wer auf einer Instanz mit einem eigenen Pfad arbeitet, gibt
    /// ihn in <paramref name="path"/> an.</para>
    /// <para>Das Präfix <c>Bearer </c> gehört zum Wert und wird — wie bei <c>apiToken</c> — genau einmal
    /// gesetzt (<see cref="TanssApiTokenAuthenticationProvider.HeaderValue"/>); ohne Präfix lehnt der
    /// Filter ab (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht
    /// geraten“).</para>
    /// </remarks>
    /// <param name="refreshToken">Das Erneuerungstoken aus
    /// <see cref="TanssLoginResult.Refresh"/>.</param>
    /// <param name="path">Die Route, über die erneuert wird — jede außer <see cref="LoginPath"/> und
    /// <see cref="DocumentedLoginPath"/>; Vorgabe <see cref="RefreshPath"/>.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <exception cref="TanssApiException">Das Erneuerungstoken ist abgelaufen oder die Antwort trug kein
    /// Paar.</exception>
    public async Task<TanssLoginResult> RefreshAsync(string refreshToken, string path = RefreshPath,
                                                     CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        HttpRequestMessage request = new(HttpMethod.Get, Address(path));

        // Abschnitt 2: refreshToken ja, apiToken nein - mit apiToken kommt 403.
        if (TanssApiTokenAuthenticationProvider.HeaderValue(refreshToken) is { } value)
        {
            request.Headers.TryAddWithoutValidation(RefreshTokenHeader, value);
        }

        (HttpStatusCode status, string body) = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (status is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw TanssApiException.FromResponse(status, body);
        }

        TanssLoginResult result = TanssLoginResult.Parse(body);
        Store(result);
        if (_logger?.IsEnabled(LogLevel.Information) == true)
        {
            _logger.LogInformation("Token über {Path} erneuert: gültig bis {ExpiresAt:u}.", path, result.ExpiresAt);
        }

        return result;
    }

    /// <summary>
    /// Prägt ein Token für ein externes Programm: <c>GET /api/v1/jwts/{ext_program}</c>.
    /// </summary>
    /// <remarks>
    /// <para>Beschreibung der Operation: „Mints a JWT that lets an external program (e.g. remote-support
    /// agent, mobile app, integration bot) authenticate against this API. The token's lifetime is
    /// <c>duration</c> (milliseconds, default 1 year), the <c>info</c> parameter is stored in the token
    /// log so the issued token can be tracked / revoked, and <c>isForTesting=true</c> skips that log
    /// entry. For <c>REMOTE_SUPPORT</c> programs, <c>info</c> must be a numeric type code &gt;= 1000.“
    /// Vorausgesetzt wird das Recht <c>CREATE_JWT_TOKENS_FOR_EXTERNAL_PROGRAMMS</c>, und das Programm
    /// muss lizenziert sein.</para>
    /// <para><b>Jeder Aufruf prägt ein neues Token und wird serverseitig protokolliert.</b> Genau deshalb
    /// nimmt die Wiederholungsregel des Clients <c>/api/v1/jwts</c> aus: Eine Wiederholung nach 429, 503
    /// oder 504 hinterließe ein zweites Token im Protokoll (<c>GET /api/v1/jwts/log</c>). Wer nur
    /// ausprobiert, setzt <paramref name="isForTesting"/>.</para>
    /// <para>Die Route verlangt die Rolle <c>USER</c>; die Kopfzeile trägt also das Sitzungstoken aus
    /// <see cref="LoginAsync"/>, nicht das geprägte.</para>
    /// </remarks>
    /// <param name="extProgram">Die Kennung des Programms, etwa <c>tanss_app</c>, <c>remote_support</c>
    /// oder <c>erp</c> (Pfadparameter <c>ext_program</c> der offiziellen Schnittstellenbeschreibung;
    /// welche Kennungen eine Instanz führt, hängt an ihren lizenzierten Programmen).</param>
    /// <param name="duration">Die Gültigkeitsdauer; <c>null</c> überlässt dem Server seine Vorgabe von
    /// einem Jahr.</param>
    /// <param name="info">Der Eintrag für das Token-Protokoll; bei <c>REMOTE_SUPPORT</c> ein Zahlencode
    /// &gt;= 1000.</param>
    /// <param name="isForTesting">Bei <c>true</c> wird kein Protokolleintrag geschrieben.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Das geprägte Token aus <c>content.apiToken</c>.</returns>
    /// <exception cref="TanssApiException">Recht oder Lizenz fehlen (403), oder die Antwort trug kein
    /// Token.</exception>
    public async Task<string> MintAsync(string extProgram, TimeSpan? duration = null, string? info = null,
                                        bool? isForTesting = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extProgram);

        List<string> query = [];
        if (duration is { } lifetime)
        {
            query.Add("duration=" + ((long)lifetime.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        }

        if (info is not null)
        {
            query.Add("info=" + Uri.EscapeDataString(info));
        }

        if (isForTesting is { } testing)
        {
            query.Add("isForTesting=" + (testing ? "true" : "false"));
        }

        string path = MintPath + "/" + Uri.EscapeDataString(extProgram)
                      + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query));

        HttpRequestMessage request = new(HttpMethod.Get, Address(path));
        if (TanssApiTokenAuthenticationProvider.HeaderValue(_tokens?.GetToken()) is { } token)
        {
            request.Headers.TryAddWithoutValidation(TanssApiTokenAuthenticationProvider.HeaderName, token);
        }

        (HttpStatusCode status, string body) = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (status is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw TanssApiException.FromResponse(status, body);
        }

        string minted = TokenOf(body);
        if (_logger?.IsEnabled(LogLevel.Information) == true)
        {
            _logger.LogInformation("Token für {Program} geprägt; Protokolleintrag: {Logged}.",
                                   extProgram, isForTesting is true ? "nein" : "ja");
        }

        return minted;
    }

    private HttpRequestMessage HeaderLogin(string username, string password, string? loginToken)
    {
        // Abschnitt 1: kein Rumpf, Zugangsdaten ausschliesslich in Kopfzeilen, und entweder
        // password oder logintoken - nie beides.
        HttpRequestMessage request = new(HttpMethod.Post, Address(LoginPath));
        request.Headers.TryAddWithoutValidation(UserHeader, EncodeCredentialHeader(username, nameof(username)));
        request.Headers.TryAddWithoutValidation(
            loginToken is null ? PasswordHeader : LoginTokenHeader,
            loginToken is null
                ? EncodeCredentialHeader(password, nameof(password))
                : EncodeCredentialHeader(loginToken, nameof(loginToken)));
        return request;
    }

    private HttpRequestMessage HeaderDashboardLogin(string dashboardApiKey)
    {
        // Abschnitt 10: nur dbapikey - kein Rumpf, kein user, kein password. Steht eine der
        // beiden anderen Kopfzeilen daneben, nimmt der Server einen anderen Zweig.
        HttpRequestMessage request = new(HttpMethod.Post, Address(LoginPath));
        request.Headers.TryAddWithoutValidation(DashboardKeyHeader,
                                                EncodeCredentialHeader(dashboardApiKey, nameof(dashboardApiKey)));
        return request;
    }

    private HttpRequestMessage DocumentedDashboardLogin(string dashboardApiKey)
    {
        // Der Server nimmt im Rumpf username, password, token und dbapikey entgegen. Fuer diesen Weg
        // zaehlt allein das letzte Feld.
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(DashboardKeyHeader, dashboardApiKey);
            writer.WriteEndObject();
        }

        return new HttpRequestMessage(HttpMethod.Post, Address(DocumentedLoginPath))
        {
            Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Bringt eine Zugangsangabe in die Form, in der der Server sie erwartet: die UTF-8-Bytes als
    /// Latin-1-Zeichenkette.
    /// </summary>
    /// <remarks>
    /// <para><b>Vom Server so umgesetzt, gegen TANSS 10.10 geprüft</b> (siehe README, Abschnitt
    /// „Gemessen, nicht geraten“): Der Server liest jede Zugangsdaten-Kopfzeile — <c>user</c>,
    /// <c>password</c>, <c>logintoken</c>, <c>dbapikey</c> —, nimmt deren <b>ISO-8859-1-Bytes</b> und
    /// dekodiert sie als <b>UTF-8</b>. Der Server rechnet also damit, dass auf der Leitung UTF-8 steht,
    /// obwohl HTTP-Kopfzeilen byteweise als Latin-1 gelesen werden.</para>
    /// <para><b>Folge für einen .NET-Client:</b> Ein Kopfzeilenwert geht byteweise als Latin-1 hinaus.
    /// Ein Kennwort mit Umlauten kommt deshalb nur dann richtig an, wenn der Client die UTF-8-Bytes
    /// vorher selbst in eine Latin-1-Zeichenkette verwandelt — genau das tut diese Methode mit
    /// <c>Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(value))</c>. Aus <c>ä</c> (U+00E4) werden so
    /// die zwei Zeichen <c>Ã¤</c> (U+00C3 U+00A4), die als die Bytes <c>C3 A4</c> auf die Leitung gehen —
    /// und das ist die UTF-8-Darstellung von <c>ä</c>, die der Server zurückrechnet.</para>
    /// <para><b>Die Verbindungsschicht muss Latin-1 auch schreiben dürfen.</b> Ein
    /// <see cref="HttpClient"/> mit einem vorgegebenen <see cref="HttpClientHandler"/> weist Kopfzeilen
    /// mit Zeichen über U+007F ab („Request headers must contain only ASCII characters“ — am 2026-09-15
    /// gegen .NET 10 nachgestellt). Deshalb liefert
    /// <see cref="TanssRestClientFactory.CreateFinalHandler"/> einen <c>SocketsHttpHandler</c> mit
    /// <c>RequestHeaderEncodingSelector = (_, _) =&gt; Encoding.Latin1</c>; die Bytes stehen damit ohne
    /// Zutun des Aufrufers auf der Leitung. Reine ASCII-Zugangsdaten sind von alledem unberührt.</para>
    /// <para><b>Das Eurozeichen lässt sich nicht übertragen.</b> Der Server ersetzt nach dem Dekodieren
    /// jedes <c>€</c> (U+20AC) durch U+0080 — das Byte, unter dem Windows-1252 das Eurozeichen führt. Ein
    /// Kennwort mit <c>€</c> kommt also selbst bei richtiger Kodierung verändert an; es taugt für diese
    /// Anmeldung nicht.</para>
    /// </remarks>
    /// <param name="value">Die Zugangsangabe, wie der Aufrufer sie kennt.</param>
    /// <param name="parameterName">Der Name des Parameters — für die Ausnahme.</param>
    /// <exception cref="ArgumentException">Der Wert trägt ein Steuerzeichen. HTTP-Kopfzeilen können
    /// keines tragen, und <see cref="HttpClient"/> weist es ab; hier wird es gemeldet, solange noch zu
    /// sehen ist, welche Angabe gemeint war.</exception>
    private static string EncodeCredentialHeader(string value, string parameterName)
    {
        foreach (char character in value)
        {
            if (character < ' ' || character == (char)0x7F)
            {
                throw new ArgumentException(
                    $"Die Zugangsangabe enthält das Steuerzeichen U+{(int)character:X4} und kann "
                    + "deshalb nicht als HTTP-Kopfzeile übertragen werden. TANSS nimmt Zugangsdaten "
                    + "ausschließlich in Kopfzeilen entgegen (gemessen gegen TANSS 10.10 am "
                    + "2026-09-15); HttpClient weist Steuerzeichen darin ab.",
                    parameterName);
            }
        }

        return Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(value));
    }

    private HttpRequestMessage DocumentedLogin(string username, string password, string? loginToken)
    {
        // components.schemas.TnsLoginCredentials: username, password, token - und nichts sonst.
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("username", username);
            writer.WriteString("password", password);
            writer.WriteString("token", loginToken ?? string.Empty);
            writer.WriteEndObject();
        }

        return new HttpRequestMessage(HttpMethod.Post, Address(DocumentedLoginPath))
        {
            Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json"),
        };
    }

    private static string TokenOf(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("content", out JsonElement content)
                && content.ValueKind == JsonValueKind.Object
                && content.TryGetProperty("apiToken", out JsonElement token)
                && token.ValueKind == JsonValueKind.String
                && token.GetString() is { Length: > 0 } value)
            {
                return value;
            }
        }
        catch (JsonException cause)
        {
            throw new TanssApiException("Die Antwort auf das Prägen war kein JSON.", cause);
        }

        throw new TanssApiException(
            "Die Antwort auf das Prägen trug kein Token. Dokumentiert ist eine „Map with the "
            + "minted JWT under the `apiToken` key“, also { meta, content: { apiToken } }.",
            HttpStatusCode.OK, errorCode: null, localizedText: null, traceId: null, rawBody: body);
    }

    private Uri Address(string path) => new(_baseAddress, path.TrimStart('/'));

    private void Store(TanssLoginResult result)
    {
        if (_tokens is MutableTokenProvider mutable)
        {
            mutable.Token = result.ApiKey;
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpRequestMessage request,
                                                                       CancellationToken cancellationToken)
    {
        // Status und Rumpf kommen roh zurueck: Ueber Erfolg entscheidet bei der Anmeldung nicht
        // der Status, sondern content.apiKey (gemessen gegen TANSS 10.10 am 2026-09-15).
        using (request)
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken)
                                                            .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (response.StatusCode, body);
        }
    }
}
