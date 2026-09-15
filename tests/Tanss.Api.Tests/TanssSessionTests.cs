using System.Net;
using System.Text;
using System.Text.Json;
using Tanss.Api.Rest;
using Tanss.Api.Rest.Models;
using Tanss.Api.Tests.Fakes;
using Xunit;

namespace Tanss.Api.Tests;

/// <summary>
/// Die drei Token-Abläufe gegen das, was an einer echten 10.10-Instanz gemessen wurde (gemessen gegen
/// TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“): Anmeldung über
/// Kopfzeilen, Erneuerung über <c>refreshToken</c>, Prägen wie dokumentiert.
/// </summary>
public sealed class TanssSessionTests
{
    /// <summary>Die gemessene Erfolgsantwort — mit dem Feld <c>warning</c>, das die Spezifikation nicht kennt.</summary>
    private const string LoginResponse = """
        {"meta":{"text":"Welcome, your ApiToken is 4 hours valid."},
         "content":{"employeeId":1,"apiKey":"Bearer xyzXyZ","expire":1789477324,
                    "refresh":"Bearer exyzXYZ","employeeType":"COMPANY_ADMIN",
                    "warning":"Ihr Kennwort laeuft in 3 Tagen ab."}}
        """;

    /// <summary>Die gemessene Antwort auf ein falsches Kennwort: HTTP 200, kein apiKey (Abschnitt 1).</summary>
    private const string DeniedResponse = """
        {"meta":{"text":"Unsuccesful login attempt"},
         "content":{"detailMessage":"LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD",
                    "stackTrace":[],"suppressedExceptions":[]}}
        """;

    /// <summary>
    /// Die am 2026-09-15 mit einem erfundenen Schlüssel gemessene Antwort auf eine
    /// Dashboard-Anmeldung: dieselbe Form wie jede Ablehnung, nur mit dem eigenen Code
    /// (Abschnitt 10).
    /// </summary>
    private const string DashboardDeniedResponse = """
        {"meta":{"text":"Unsuccesful login attempt"},
         "content":{"detailMessage":"LOGIN_ERROR_NO_USER_FOR_DASHBOARD",
                    "stackTrace":[],"suppressedExceptions":[]}}
        """;

    [Fact]
    public async Task Angemeldet_wird_ueber_user_login_mit_Kopfzeilen_und_ohne_Rumpf()
    {
        // Abschnitt 1: POST /api/v1/user/login, Zugangsdaten in den Kopfzeilen user und
        // password, kein Rumpf. Der Server liest nur Kopfzeilen.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        MutableTokenProvider tokens = new();
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, tokens);

        TanssLoginResult result = await session.LoginAsync("sm", "secret password");

        CapturedRequest sent = handler.Last;
        Assert.Equal("POST", sent.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/user/login", sent.Uri.ToString());
        Assert.Equal("sm", sent.Header("user"));
        Assert.Equal("secret password", sent.Header("password"));
        Assert.False(sent.HasHeader("logintoken"));
        Assert.Null(sent.Body);

        // security: [] - weder apiToken noch Authorization gehen mit.
        Assert.Null(sent.ApiToken);
        Assert.False(sent.HasAuthorizationHeader);

        Assert.Equal(1, result.EmployeeId);
        Assert.Equal("Bearer xyzXyZ", result.ApiKey);
        Assert.Equal(1789477324, result.Expire);
        Assert.Equal("Bearer exyzXYZ", result.Refresh);
        Assert.Equal("COMPANY_ADMIN", result.EmployeeType);
        // Abschnitt 1: content.warning steht in keinem Beispiel der Spezifikation.
        Assert.Equal("Ihr Kennwort laeuft in 3 Tagen ab.", result.Warning);
        Assert.Equal(TanssSession.LoginPath, result.LoginPath);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789477324), result.ExpiresAt);

        // Der Schlüssel liegt danach im veränderlichen Anbieter.
        Assert.Equal("Bearer xyzXyZ", tokens.Token);
    }

    [Fact]
    public async Task Ein_Anmeldetoken_tritt_an_die_Stelle_des_Kennworts()
    {
        // Abschnitt 1: user+password ODER user+logintoken ODER dbapikey - nie password und
        // logintoken zusammen.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        await session.LoginAsync("sm", "secret password", "123456");

        CapturedRequest sent = handler.Last;
        Assert.Equal("sm", sent.Header("user"));
        Assert.Equal("123456", sent.Header("logintoken"));
        Assert.False(sent.HasHeader("password"));
    }

    [Fact]
    public async Task Die_Dashboard_Anmeldung_schickt_allein_die_Kopfzeile_dbapikey()
    {
        // Der Server nimmt den Dashboard-Zweig nur, wenn die Anfrage ausschliesslich dbapikey traegt -
        // kein user, kein password, kein Rumpf. Und wie jede Anmeldung traegt sie kein apiToken.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        MutableTokenProvider tokens = new();
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, tokens);

        TanssLoginResult result = await session.LoginWithDashboardKeyAsync("d4shb0ard-key");

        CapturedRequest sent = handler.Last;
        Assert.Equal("POST", sent.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/user/login", sent.Uri.ToString());
        Assert.Equal("d4shb0ard-key", sent.Header("dbapikey"));
        Assert.False(sent.HasHeader("user"));
        Assert.False(sent.HasHeader("password"));
        Assert.False(sent.HasHeader("logintoken"));
        Assert.Null(sent.Body);
        Assert.Null(sent.ApiToken);
        Assert.False(sent.HasAuthorizationHeader);

        // Heraus kommt ein gewoehnliches Anmeldeergebnis; der Anspruch dashboardLogin steckt im
        // Token selbst und nicht im Umschlag.
        Assert.Equal("Bearer xyzXyZ", result.ApiKey);
        Assert.Equal(1, result.EmployeeId);
        Assert.Equal(TanssSession.LoginPath, result.LoginPath);
        Assert.Equal("Bearer xyzXyZ", tokens.Token);
    }

    [Fact]
    public async Task Eine_404_fuehrt_die_Dashboard_Anmeldung_auf_das_Feld_dbapikey()
    {
        // Der Server nimmt im Rumpf neben username, password und token auch dbapikey - das Schema der
        // Spezifikation kennt nur die ersten drei. Fuer diesen Weg zaehlt allein das vierte Feld.
        using RecordingHandler handler = new(request =>
            request.Uri.AbsolutePath.EndsWith("/api/v1/user/login", StringComparison.Ordinal)
                ? RecordingHandler.Respond(HttpStatusCode.NotFound, string.Empty)
                : RecordingHandler.Respond(HttpStatusCode.OK, LoginResponse));
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        TanssLoginResult result = await session.LoginWithDashboardKeyAsync("d4shb0ard-key");

        Assert.Equal(2, handler.Calls);
        CapturedRequest documented = handler.Last;
        Assert.Equal("POST", documented.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/login", documented.Uri.ToString());
        Assert.Null(documented.ApiToken);

        using JsonDocument body = JsonDocument.Parse(documented.Body!);
        Assert.Equal(["dbapikey"], body.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("d4shb0ard-key", body.RootElement.GetProperty("dbapikey").GetString());

        Assert.Equal(TanssSession.DocumentedLoginPath, result.LoginPath);
    }

    [Fact]
    public async Task Ein_unbekannter_Dashboard_Schluessel_meldet_LOGIN_ERROR_NO_USER_FOR_DASHBOARD()
    {
        // Abschnitt 10, live gemessen: ein erfundener Schluessel antwortet mit HTTP 200,
        // meta.text "Unsuccesful login attempt" und content.detailMessage
        // LOGIN_ERROR_NO_USER_FOR_DASHBOARD.
        using RecordingHandler handler = new(HttpStatusCode.OK, DashboardDeniedResponse);
        MutableTokenProvider tokens = new("Bearer altes.token");
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, tokens);

        TanssApiException error = await Assert.ThrowsAsync<TanssApiException>(
            () => session.LoginWithDashboardKeyAsync("gibt-es-nicht"));

        Assert.Equal("LOGIN_ERROR_NO_USER_FOR_DASHBOARD", error.ErrorCode);
        Assert.Equal(TnsErrorCode.LOGIN_ERROR_NO_USER_FOR_DASHBOARD, error.KnownErrorCode);
        Assert.Equal("Unsuccesful login attempt", error.Message);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        // Ein fehlgeschlagener Versuch fasst das vorhandene Token nicht an.
        Assert.Equal("Bearer altes.token", tokens.Token);
    }

    [Fact]
    public async Task Umlaute_reisen_als_Latin_1_Abbild_ihrer_UTF_8_Bytes()
    {
        // Der Server liest die ISO-8859-1-Bytes der Kopfzeile und dekodiert sie als UTF-8. Ein Kennwort
        // mit Umlauten kommt also nur an, wenn auf der Leitung die UTF-8-Bytes stehen - "Paßwortäöü" also
        // als 50 61 C3 9F 77 6F 72 74 C3 A4 C3 B6 C3 BC.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        await session.LoginAsync("müller", "Paßwortäöü");

        CapturedRequest sent = handler.Last;
        byte[] password = Encoding.Latin1.GetBytes(sent.Header("password")!);
        Assert.Equal(new byte[] { 0x50, 0x61, 0xC3, 0x9F, 0x77, 0x6F, 0x72, 0x74,
                                  0xC3, 0xA4, 0xC3, 0xB6, 0xC3, 0xBC }, password);
        Assert.Equal("Paßwortäöü", Encoding.UTF8.GetString(password));

        byte[] user = Encoding.Latin1.GetBytes(sent.Header("user")!);
        Assert.Equal(new byte[] { 0x6D, 0xC3, 0xBC, 0x6C, 0x6C, 0x65, 0x72 }, user);
        Assert.Equal("müller", Encoding.UTF8.GetString(user));

        // Das ist genau die Kodierung, die Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(…))
        // erzeugt - und nicht der wörtliche Wert.
        Assert.Equal(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("Paßwortäöü")), sent.Header("password"));
        Assert.NotEqual("Paßwortäöü", sent.Header("password"));
    }

    [Fact]
    public async Task Auch_das_Anmeldetoken_und_der_Dashboard_Schluessel_werden_so_kodiert()
    {
        // Dieselbe Regel gilt fuer logintoken und dbapikey: der Server liest alle Zugangsdaten-Kopfzeilen
        // gleich.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        await session.LoginAsync("sm", "egal", "tökén");
        Assert.Equal(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("tökén")), handler.Last.Header("logintoken"));

        await session.LoginWithDashboardKeyAsync("schlüssel");
        Assert.Equal(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("schlüssel")), handler.Last.Header("dbapikey"));
    }

    [Fact]
    public async Task Ein_Eurozeichen_geht_richtig_hinaus_und_kommt_trotzdem_veraendert_an()
    {
        // Abschnitt 11: Der Client schickt das Eurozeichen korrekt als UTF-8 (E2 82 AC). der Server
        // ersetzt es nach dem Dekodieren aber durch U+0080 - ein Kennwort mit € ist ueber diese Anmeldung
        // also nicht uebertragbar. Dieser Test haelt beides fest.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        await session.LoginAsync("sm", "€uro");

        byte[] wire = Encoding.Latin1.GetBytes(handler.Last.Header("password")!);
        Assert.Equal(new byte[] { 0xE2, 0x82, 0xAC, 0x75, 0x72, 0x6F }, wire);
        Assert.Equal("€uro", Encoding.UTF8.GetString(wire));

        // Was der Server daraus macht (nachgestellt, nicht gemessen): das Eurozeichen wird zu
        // U+0080, der Rest bleibt.
        string beimServer = Encoding.UTF8.GetString(wire).Replace("\u20ac", "\u0080", StringComparison.Ordinal);
        Assert.Equal("\u0080uro", beimServer);
        Assert.NotEqual("\u20acuro", beimServer);
    }

    [Fact]
    public async Task Ein_Steuerzeichen_in_den_Zugangsdaten_wird_klar_gemeldet()
    {
        // Eine Kopfzeile kann kein Steuerzeichen tragen; HttpClient wiese den Wert ab. Gemeldet
        // wird das, solange noch zu sehen ist, welche Angabe gemeint war.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => session.LoginAsync("sm", "zwei\nZeilen"));

        Assert.Equal("password", error.ParamName);
        Assert.Contains("Steuerzeichen U+000A", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);

        ArgumentException key = await Assert.ThrowsAsync<ArgumentException>(
            () => session.LoginWithDashboardKeyAsync("mit\tTabulator"));

        Assert.Equal("dashboardApiKey", key.ParamName);
        Assert.Contains("Steuerzeichen U+0009", key.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_404_fuehrt_auf_den_dokumentierten_Weg_mit_JSON_Rumpf()
    {
        // Auf 10.10 gibt es /api/v1/login nicht; auf einer Fassung ohne /api/v1/user/login
        // gilt das Umgekehrte. Antwortet der gemessene Weg 404, wird der dokumentierte genommen.
        using RecordingHandler handler = new(request =>
            request.Uri.AbsolutePath.EndsWith("/api/v1/user/login", StringComparison.Ordinal)
                ? RecordingHandler.Respond(HttpStatusCode.NotFound, string.Empty)
                : RecordingHandler.Respond(HttpStatusCode.OK, LoginResponse));
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        TanssLoginResult result = await session.LoginAsync("sm", "secret password", "123456");

        Assert.Equal(2, handler.Calls);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/user/login", handler.Captured[0].Uri.ToString());

        CapturedRequest documented = handler.Last;
        Assert.Equal("POST", documented.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/login", documented.Uri.ToString());
        Assert.Null(documented.ApiToken);

        // TnsLoginCredentials: username, password, token - und nichts sonst.
        using JsonDocument body = JsonDocument.Parse(documented.Body!);
        Assert.Equal(["username", "password", "token"], body.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("sm", body.RootElement.GetProperty("username").GetString());
        Assert.Equal("secret password", body.RootElement.GetProperty("password").GetString());
        Assert.Equal("123456", body.RootElement.GetProperty("token").GetString());

        Assert.Equal(TanssSession.DocumentedLoginPath, result.LoginPath);
        Assert.Equal("Bearer xyzXyZ", result.ApiKey);
    }

    [Fact]
    public async Task Eine_abgelehnte_Anmeldung_ist_eine_200_und_trotzdem_ein_Fehler()
    {
        // Abschnitt 1: falsches Kennwort -> HTTP 200 mit detailMessage. Der Statuscode taugt
        // nicht zur Fehlererkennung; massgeblich ist content.apiKey.
        using RecordingHandler handler = new(HttpStatusCode.OK, DeniedResponse);
        MutableTokenProvider tokens = new("Bearer altes.token");
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, tokens);

        TanssApiException error = await Assert.ThrowsAsync<TanssApiException>(
            () => session.LoginAsync("sm", "falsch"));

        Assert.Equal("LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD", error.ErrorCode);
        Assert.Equal("Unsuccesful login attempt", error.Message);
        Assert.Equal("Unsuccesful login attempt", error.LocalizedText);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Contains("detailMessage", error.RawBody!, StringComparison.Ordinal);
        // Ein fehlgeschlagener Versuch fasst das vorhandene Token nicht an.
        Assert.Equal("Bearer altes.token", tokens.Token);
    }

    [Fact]
    public async Task Ein_leerer_Rumpf_ist_ebenfalls_ein_Fehler()
    {
        // Abschnitt 1: Anmeldung ohne die erwarteten Kopfzeilen -> HTTP 200 mit leerem Rumpf.
        using RecordingHandler handler = new(HttpStatusCode.OK, string.Empty);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        TanssApiException error = await Assert.ThrowsAsync<TanssApiException>(
            () => session.LoginAsync("sm", "secret password"));

        Assert.Equal(TanssLoginResult.EmptyResponseCode, error.ErrorCode);
        Assert.Contains("leerem Rumpf", error.Message, StringComparison.Ordinal);
        Assert.Contains("gemessen gegen TANSS 10.10", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Anmeldung_wird_nicht_wiederholt()
    {
        // Wiederholt wird nur Lesendes: die Anmeldung ist ein POST und geht genau einmal hinaus.
        using RecordingHandler handler = new(HttpStatusCode.ServiceUnavailable, string.Empty);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        TanssApiException error = await Assert.ThrowsAsync<TanssApiException>(
            () => session.LoginAsync("sm", "secret password"));

        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task Erneuert_wird_ueber_die_Kopfzeile_refreshToken_und_ohne_apiToken()
    {
        // Abschnitt 2: Erneuerungstoken in apiToken -> 403; in refreshToken, ohne apiToken ->
        // 200 mit vollstaendigem Anmeldeergebnis.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        MutableTokenProvider tokens = new("Bearer altes.token");
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, tokens);

        TanssLoginResult result = await session.RefreshAsync("Bearer exyzXYZ");

        CapturedRequest sent = handler.Last;
        Assert.Equal("GET", sent.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/employees/ownState?loggedInUserId=42", sent.Uri.ToString());
        Assert.Equal("Bearer exyzXYZ", sent.Header("refreshToken"));
        // Die Schreibweise ist gleichgueltig - hier wird geprueft, dass apiToken fehlt.
        Assert.Null(sent.ApiToken);
        Assert.False(sent.HasHeader("apiToken"));
        Assert.False(sent.HasAuthorizationHeader);

        // Die Antwort "looks similar to a usual login" - und das neue Paar wird uebernommen.
        Assert.Equal("Bearer xyzXyZ", result.ApiKey);
        Assert.Equal("Bearer exyzXYZ", result.Refresh);
        Assert.Equal("Bearer xyzXyZ", tokens.Token);
        // Erneuert wurde, nicht angemeldet: der Anmeldeweg bleibt leer.
        Assert.Null(result.LoginPath);
    }

    [Fact]
    public async Task Das_Erneuerungstoken_bekommt_sein_Praefix_genau_einmal()
    {
        // Abschnitt 3: ohne "Bearer " lehnt der Filter ab. Ein von Hand kopiertes JWT wird
        // deshalb ergaenzt, ein vollstaendiger Wert unveraendert gesendet.
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        await session.RefreshAsync("eyJhbGciOiJSUzI1NiJ9.refresh");

        Assert.Equal("Bearer eyJhbGciOiJSUzI1NiJ9.refresh", handler.Last.Header("refreshToken"));
    }

    [Fact]
    public async Task Erneuern_geht_auch_ueber_einen_selbst_gewaehlten_Pfad()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, LoginResponse);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        await session.RefreshAsync("Bearer exyzXYZ", "/api/v1/tickets/own");

        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/tickets/own?loggedInUserId=42", handler.Last.Uri.ToString());
    }

    [Fact]
    public async Task Eine_abgelehnte_Erneuerung_meldet_den_leeren_Rumpf()
    {
        // Abschnitt 3: Ablehnungen kommen ohne jeden Inhalt.
        using RecordingHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http);

        TanssApiException error = await Assert.ThrowsAsync<TanssApiException>(
            () => session.RefreshAsync("Bearer abgelaufen"));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("leerem Rumpf", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Praegen_ruft_die_Route_mit_den_dokumentierten_Abfrageparametern()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK,
            """{"meta":{},"content":{"apiToken":"eyJhbGciOiJSUzI1NiJ9.signature"}}""");
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, new FakeTokenProvider(TestEnvironment.Token));

        string minted = await session.MintAsync("remote_support", TimeSpan.FromDays(365), "1000", isForTesting: true);

        CapturedRequest sent = handler.Last;
        Assert.Equal("GET", sent.Method);
        Assert.Equal("/backend/api/v1/jwts/remote_support", sent.Uri.AbsolutePath);
        Assert.Equal("31536000000", sent.Query("duration"));
        Assert.Equal("1000", sent.Query("info"));
        Assert.Equal("true", sent.Query("isForTesting"));
        // Die Route traegt die Rolle USER; das Sitzungstoken geht mit, der Mitarbeiter auch.
        Assert.Equal("42", sent.Query("loggedInUserId"));
        Assert.Equal("Bearer test.token.value", sent.ApiToken);
        Assert.Equal("eyJhbGciOiJSUzI1NiJ9.signature", minted);
    }

    [Fact]
    public async Task Ohne_Angaben_bleibt_dem_Server_seine_Vorgabe_von_einem_Jahr()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, """{"meta":{},"content":{"apiToken":"eyJ.x"}}""");
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, new FakeTokenProvider(TestEnvironment.Token));

        await session.MintAsync("tanss_app");

        CapturedRequest sent = handler.Last;
        Assert.Null(sent.Query("duration"));
        Assert.Null(sent.Query("info"));
        Assert.Null(sent.Query("isForTesting"));
        Assert.Equal("/backend/api/v1/jwts/tanss_app", sent.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Praegen_wird_nie_wiederholt()
    {
        // Jeder Aufruf praegt ein neues, protokolliertes Token: eine 503 fuehrt zum Abbruch,
        // nicht zu einem zweiten Versuch.
        using RecordingHandler handler = new(HttpStatusCode.ServiceUnavailable, """{"error":{"text":"TOO_MANY_REQUESTS"}}""");
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options(), handler: handler);
        TanssSession session = new(http, new FakeTokenProvider(TestEnvironment.Token));

        TanssApiException error = await Assert.ThrowsAsync<TanssApiException>(() => session.MintAsync("tanss_app"));

        Assert.Equal(1, handler.Calls);
        Assert.Equal("TOO_MANY_REQUESTS", error.ErrorCode);
    }

    [Fact]
    public void Ohne_Basisadresse_gibt_es_keine_Sitzung()
    {
        using HttpClient http = new();

        Assert.Throws<ArgumentException>(() => new TanssSession(http));
    }
}
