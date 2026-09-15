using System.Net;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Tanss.Api.Rest;
using Tanss.Api.Rest.Api.V1.Tickets.Own;
using Tanss.Api.Rest.Models;
using Tanss.Api.Tests.Fakes;
using Xunit;

namespace Tanss.Api.Tests.Rest;

/// <summary>
/// Der erzeugte Client hält, über die Fabrik gebaut, die Regeln der Schnittstelle ein:
/// Kopfzeile, <c>loggedInUserId</c>, Basisadresse, Wiederholung.
/// </summary>
public sealed class ClientFactoryTests
{
    [Fact]
    public async Task Eigene_Tickets_gehen_mit_loggedInUserId_und_apiToken_an_die_Basisadresse()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        FakeTokenProvider tokens = new(TestEnvironment.Token);
        TanssRestClient client = TanssRestClientFactory.Create(TestEnvironment.Options(), tokens, handler: handler);

        OwnGetResponse? response = await client.Api.V1.Tickets.Own.GetAsync();

        Assert.NotNull(response);
        Assert.Equal(1, handler.Calls);
        CapturedRequest sent = handler.Last;
        Assert.Equal("GET", sent.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/tickets/own?loggedInUserId=42", sent.Uri.ToString());
        Assert.Equal("Bearer test.token.value", sent.ApiToken);
        Assert.False(sent.HasAuthorizationHeader);
        Assert.Equal(1, tokens.Reads);
    }

    [Fact]
    public async Task Das_Token_wird_bei_jeder_Anfrage_frisch_gelesen()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        FakeTokenProvider tokens = new(n => $"Bearer token.nummer.{n}");
        TanssRestClient client = TanssRestClientFactory.Create(TestEnvironment.Options(), tokens, handler: handler);

        await client.Api.V1.Tickets.Own.GetAsync();
        await client.Api.V1.Tickets.Own.GetAsync();

        Assert.Equal(["Bearer token.nummer.1", "Bearer token.nummer.2"],
                     handler.Captured.Select(c => c.ApiToken));
    }

    [Fact]
    public async Task Ein_Token_ohne_Praefix_bekommt_genau_ein_Bearer()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(), new FakeTokenProvider("test.token.value"), handler: handler);

        await client.Api.V1.Tickets.Own.GetAsync();

        Assert.Equal("Bearer test.token.value", handler.Last.ApiToken);
    }

    [Fact]
    public async Task Ohne_Token_geht_die_Anfrage_ohne_Kopfzeile_hinaus()
    {
        // Die Spezifikation kennt tokenfreie Routen (POST /api/v1/login traegt security: []);
        // eine fehlende Kopfzeile ist deshalb kein Fehler des Clients, sondern eine 403 des
        // Servers - falls die Route ueberhaupt eine verlangt.
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(), new FakeTokenProvider(token: null), handler: handler);

        await client.Api.V1.Tickets.Own.GetAsync();

        Assert.Equal(1, handler.Calls);
        Assert.Null(handler.Last.ApiToken);
        Assert.False(handler.Last.HasAuthorizationHeader);
    }

    [Fact]
    public async Task Integrationsschnittstelle_bekommt_keinen_loggedInUserId()
    {
        // Die Regel haengt am Adapter, nicht am erzeugten Client: auch eine von Hand gebaute
        // Anfrage bekommt auf tanss.x keinen loggedInUserId - dort gilt die Rolle TANSS_APP.
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        using HttpClientRequestAdapter adapter = TanssRestClientFactory.CreateRequestAdapter(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);
        RequestInformation request = new(Method.GET, "{+baseurl}/api/tanss.x/v1/remoteSupports/systems",
                                         new Dictionary<string, object>());

        await adapter.SendNoContentAsync(request);

        Assert.Equal(TestEnvironment.BaseUrl + "/api/tanss.x/v1/remoteSupports/systems", handler.Last.Uri.ToString());
        Assert.Null(handler.Last.Query("loggedInUserId"));
        Assert.Equal("Bearer test.token.value", handler.Last.ApiToken);
    }

    [Fact]
    public async Task Erp_Schnittstelle_bekommt_keinen_loggedInUserId()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);

        CustomersCombine? response = await client.Api.Erp.V1.Customers.GetAsync();

        Assert.NotNull(response);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/erp/v1/customers", handler.Last.Uri.ToString());
        Assert.Null(handler.Last.Query("loggedInUserId"));
    }

    [Fact]
    public async Task Ohne_Mitarbeiter_Id_wird_nichts_angehaengt()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(employeeId: null), new FakeTokenProvider(TestEnvironment.Token), handler: handler);

        await client.Api.V1.Tickets.Own.GetAsync();

        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/tickets/own", handler.Last.Uri.ToString());
    }

    [Fact]
    public async Task Schreibende_Aufrufe_werden_bei_503_nicht_wiederholt()
    {
        // TANSS dedupliziert nicht: ein zweiter POST waere ein zweiter Datensatz.
        using RecordingHandler handler = new(HttpStatusCode.ServiceUnavailable, """{"error":{"text":"UNKNOWN_ERROR"}}""");
        using HttpClientRequestAdapter adapter = TanssRestClientFactory.CreateRequestAdapter(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);
        RequestInformation request = new(Method.POST, "{+baseurl}/api/v1/supports", new Dictionary<string, object>());

        await Assert.ThrowsAsync<ApiException>(() => adapter.SendNoContentAsync(request));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Token_praegen_wird_trotz_GET_nicht_wiederholt()
    {
        // GET /api/v1/jwts/{ext_program} praegt bei jedem Aufruf ein neues, protokolliertes Token.
        using RecordingHandler handler = new(_ => RetryLater());
        using HttpClientRequestAdapter adapter = TanssRestClientFactory.CreateRequestAdapter(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);
        RequestInformation request = new(Method.GET, "{+baseurl}/api/v1/jwts/tanss_app", new Dictionary<string, object>());

        await Assert.ThrowsAsync<ApiException>(() => adapter.SendNoContentAsync(request));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Lesende_Aufrufe_werden_bei_503_wiederholt()
    {
        using RecordingHandler handler = new(captured => captured.Attempt == 1
            ? RetryLater()
            : RecordingHandler.Respond(HttpStatusCode.OK, TestEnvironment.EmptyList));
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);

        OwnGetResponse? response = await client.Api.V1.Tickets.Own.GetAsync();

        Assert.NotNull(response);
        Assert.Equal(2, handler.Calls);
        Assert.All(handler.Captured, c => Assert.Equal("42", c.Query("loggedInUserId")));
    }

    [Theory]
    [InlineData("ftp://tanss.example.invalid/backend")]
    [InlineData("/backend")]
    public void Eine_untaugliche_Basisadresse_ist_ein_Einstellungsfehler(string baseUrl)
    {
        TanssApiOptions options = new()
        {
            BaseUrl = new Uri(baseUrl, UriKind.RelativeOrAbsolute),
            EmployeeId = 42,
        };

        Assert.Throws<ArgumentException>(
            () => TanssRestClientFactory.Create(options, new FakeTokenProvider(TestEnvironment.Token)));
    }

    [Fact]
    public void Basisadresse_mit_Schraegstrich_am_Ende_ergibt_keinen_doppelten()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssApiOptions options = new() { BaseUrl = new Uri(TestEnvironment.BaseUrl + "/"), EmployeeId = 42 };
        using HttpClientRequestAdapter adapter = TanssRestClientFactory.CreateRequestAdapter(
            options, new FakeTokenProvider(TestEnvironment.Token), handler: handler);

        Assert.Equal(TestEnvironment.BaseUrl, adapter.BaseUrl);
    }

    [Fact]
    public void Der_HttpClient_traegt_Basisadresse_und_Zeitgrenze()
    {
        using HttpClient http = TanssRestClientFactory.CreateHttpClient(TestEnvironment.Options());

        Assert.Equal(new Uri(TestEnvironment.BaseUrl + "/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(5), http.Timeout);
    }

    [Fact]
    public void Zeitgrenze_Proxy_und_TLS_Pruefung_kommen_aus_den_Einstellungen()
    {
        TanssApiOptions options = new()
        {
            BaseUrl = new Uri(TestEnvironment.BaseUrl),
            EmployeeId = 42,
            ValidateTls = false,
            Proxy = new Uri("http://proxy.example.invalid:3128"),
            ProxyUser = "u",
            ProxyPassword = "p",
        };

        using SocketsHttpHandler handler = TanssRestClientFactory.CreateFinalHandler(options);

        Assert.True(handler.UseProxy);
        WebProxy proxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal(new Uri("http://proxy.example.invalid:3128"), proxy.Address);
        Assert.NotNull(proxy.Credentials);
        Assert.NotNull(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.False(handler.AllowAutoRedirect);

        // Zugangsdaten reisen als Latin-1-Bytes ihrer UTF-8-Darstellung (gemessen gegen
        // TANSS 10.10 am 2026-09-15); ohne diese Wahl wiese .NET jedes Zeichen ueber U+007F ab.
        Assert.NotNull(handler.RequestHeaderEncodingSelector);
        Assert.Equal(Encoding.Latin1, handler.RequestHeaderEncodingSelector("password", new HttpRequestMessage()));

        using SocketsHttpHandler strict = TanssRestClientFactory.CreateFinalHandler(TestEnvironment.Options());
        Assert.Null(strict.SslOptions.RemoteCertificateValidationCallback);
        Assert.False(strict.UseProxy && strict.Proxy is not null);
    }

    private static HttpResponseMessage RetryLater()
    {
        HttpResponseMessage response = RecordingHandler.Respond(HttpStatusCode.ServiceUnavailable, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }
}
