using System.Net;
using Tanss.Api.Rest.Api.V1.Tickets.Own;
using Tanss.Api.Tests.Fakes;
using Xunit;

namespace Tanss.Api.Tests;

/// <summary>
/// Der eine Einstieg: erzeugter Client und Sitzung senden über dieselbe Verbindungsschicht nach
/// denselben Regeln.
/// </summary>
public sealed class TanssApiTests
{
    [Fact]
    public async Task Ein_Aufruf_ueber_Rest_haelt_Kopfzeile_und_loggedInUserId_ein()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        FakeTokenProvider tokens = new(TestEnvironment.Token);
        using TanssApi api = TanssApi.Create(TestEnvironment.Options(), tokens, handler: handler);

        OwnGetResponse? response = await api.Rest.Api.V1.Tickets.Own.GetAsync();

        Assert.NotNull(response);
        Assert.Equal(1, handler.Calls);
        CapturedRequest sent = handler.Last;
        Assert.Equal("GET", sent.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/tickets/own?loggedInUserId=42", sent.Uri.ToString());
        Assert.Equal("Bearer test.token.value", sent.ApiToken);
        Assert.False(sent.HasAuthorizationHeader);
    }

    [Fact]
    public void Alle_Teile_haengen_am_Einstieg_und_sonst_nichts()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        FakeTokenProvider tokens = new(TestEnvironment.Token);
        TanssApiOptions options = TestEnvironment.Options();
        using TanssApi api = TanssApi.Create(options, tokens, handler: handler);

        Assert.Same(options, api.Options);
        Assert.Same(tokens, api.Tokens);
        Assert.NotNull(api.Rest);
        Assert.NotNull(api.Session);

        // Genau vier Eigenschaften - keine Repositories, keine Bequemlichkeiten.
        Assert.Equal(["Options", "Rest", "Session", "Tokens"],
                     typeof(TanssApi).GetProperties().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Sitzung_und_Rest_senden_ueber_dieselbe_Verbindungsschicht()
    {
        using RecordingHandler handler = new(_ => RecordingHandler.Respond(HttpStatusCode.OK, Login));
        MutableTokenProvider tokens = new();
        using TanssApi api = TanssApi.Create(TestEnvironment.Options(), tokens, handler: handler);

        await api.Session.LoginAsync("sb", "secret password");
        await api.Rest.Api.V1.Tickets.Own.GetAsync();

        Assert.Equal(2, handler.Calls);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/user/login", handler.Captured[0].Uri.ToString());
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/tickets/own?loggedInUserId=42",
                     handler.Captured[1].Uri.ToString());
        // Das Token aus der Anmeldung liegt im Anbieter und wird sofort mitgeschickt.
        Assert.Equal("Bearer xyzXyZ", tokens.Token);
        Assert.Equal("Bearer xyzXyZ", handler.Captured[1].ApiToken);
    }

    [Theory]
    [InlineData("ftp://tanss.example.invalid/backend")]
    [InlineData("tanss.example.invalid/backend")]
    public void Eine_untaugliche_Basisadresse_ist_ein_Einstellungsfehler(string baseUrl)
    {
        TanssApiOptions options = new()
        {
            BaseUrl = new Uri(baseUrl, UriKind.RelativeOrAbsolute),
            EmployeeId = 42,
        };

        Assert.Throws<ArgumentException>(
            () => TanssApi.Create(options, new FakeTokenProvider(TestEnvironment.Token)));
    }

    private const string Login = """
        {"meta":{"text":"Welcome, your ApiToken is 4 hours valid."},
         "content":{"employeeId":1,"apiKey":"Bearer xyzXyZ","expire":1563963819,
                    "refresh":"Bearer exyzXYZ","employeeType":"CUSTOMER"}}
        """;
}
