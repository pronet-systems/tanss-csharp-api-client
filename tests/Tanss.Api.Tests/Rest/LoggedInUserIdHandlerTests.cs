using System.Net;
using Tanss.Api.Rest;
using Tanss.Api.Tests.Fakes;
using Xunit;

namespace Tanss.Api.Tests.Rest;

/// <summary>
/// Die Regel des Servers, allein und ohne Kiota: Der Parameter kommt auf jede Route, deren Rolle
/// <c>USER</c> einschließt — und nur dorthin.
/// </summary>
public sealed class LoggedInUserIdHandlerTests
{
    [Theory]
    // USER-Module unter /api/v1 (TokenRoles: TokenRole.User).
    [InlineData("https://h/backend/api/v1/timers", "42")]
    [InlineData("https://h/backend/api/v1/tickets/own", "42")]
    [InlineData("https://h/backend/api/v1/timers?from=1", "42")]
    [InlineData("https://h/backend/api/v1/jwts/tanss_app", "42")]
    // Gemischte Zeilen schliessen USER ein.
    [InlineData("https://h/backend/api/v1/offers/17", "42")]
    [InlineData("https://h/backend/api/v1/cache", "42")]
    // Vom Aufrufer gesetzt, in jeder Schreibweise: bleibt stehen, wird nicht verdoppelt.
    [InlineData("https://h/backend/api/v1/timers?loggedInUserId=7", "7")]
    [InlineData("https://h/backend/api/v1/timers?LOGGEDINUSERID=7", null)]
    // Ohne Token kein Mitarbeiter: die Anmeldung bekommt ihn nicht.
    [InlineData("https://h/backend/api/v1/login", null)]
    [InlineData("https://h/backend/.well-known/jwks.json", null)]
    // Modul-Praefixe: eigene Rolle, kein Mitarbeiterkontext.
    [InlineData("https://h/backend/api/tanss.x/v1/technicians", null)]
    [InlineData("https://h/backend/api/tanss.app/v1/supports", null)]
    [InlineData("https://h/backend/api/erp/v1/customers", null)]
    [InlineData("https://h/backend/api/remoteSupports/v1/remoteSupports", null)]
    [InlineData("https://h/backend/api/timestamps/v1/x", null)]
    // Gesperrt (in der Sicherheitskonfiguration von 10.10 passt keine Regel): erst recht nichts anhaengen.
    [InlineData("https://h/backend/api/v1/sla", null)]
    public async Task Der_Parameter_folgt_der_Rolle(string url, string? expected)
    {
        CapturedRequest sent = await Send(url);

        Assert.Equal(expected, sent.Query("loggedInUserId"));
    }

    [Fact]
    public async Task Ohne_Mitarbeiter_Id_bleibt_die_Adresse_unveraendert()
    {
        CapturedRequest sent = await Send("https://h/backend/api/v1/timers", employeeId: null);

        Assert.Equal("https://h/backend/api/v1/timers", sent.Uri.ToString());
    }

    [Fact]
    public async Task Vorhandene_Parameter_bleiben_unangetastet()
    {
        CapturedRequest sent = await Send("https://h/backend/api/v1/timers?from=1&till=2");

        Assert.Equal("https://h/backend/api/v1/timers?from=1&till=2&loggedInUserId=42", sent.Uri.ToString());
    }

    [Fact]
    public async Task Abweichende_Schreibweise_wird_nicht_verdoppelt()
    {
        CapturedRequest sent = await Send("https://h/backend/api/v1/timers?LoggedInUserId=7");

        Assert.Equal("https://h/backend/api/v1/timers?LoggedInUserId=7", sent.Uri.ToString());
    }

    [Theory]
    [InlineData("https://h/api/v1/timers", "")]
    // Passt die Basis nicht, zaehlt das erste /api/: die Regel darf nicht stumm aussetzen.
    [InlineData("https://h/anderswo/api/v1/timers", "/backend")]
    public async Task Die_Basis_wird_abgeschnitten_oder_das_erste_api_gesucht(string url, string basePath)
    {
        CapturedRequest sent = await Send(url, basePath: basePath);

        Assert.Equal("42", sent.Query("loggedInUserId"));
    }

    [Fact]
    public async Task Aus_den_Einstellungen_gebaut_kennt_er_Mitarbeiter_und_Basis()
    {
        using RecordingHandler inner = new(HttpStatusCode.OK, "{}");
        using LoggedInUserIdHandler handler = new(TestEnvironment.Options()) { InnerHandler = inner };
        using HttpClient http = new(handler, disposeHandler: false);

        using HttpResponseMessage _ = await http.GetAsync(new Uri(TestEnvironment.BaseUrl + "/api/v1/timers"));

        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/timers?loggedInUserId=42", inner.Last.Uri.ToString());
    }

    private static async Task<CapturedRequest> Send(string url, int? employeeId = 42, string basePath = "/backend")
    {
        using RecordingHandler inner = new(HttpStatusCode.OK, "{}");
        using LoggedInUserIdHandler handler = new(employeeId, basePath) { InnerHandler = inner };
        using HttpClient http = new(handler, disposeHandler: false);

        using HttpResponseMessage _ = await http.GetAsync(new Uri(url));
        return inner.Last;
    }
}
