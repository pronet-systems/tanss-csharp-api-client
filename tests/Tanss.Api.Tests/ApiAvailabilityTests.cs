using Xunit;

namespace Tanss.Api.Tests;

/// <summary>
/// Der eingebettete Endpunkt-Index: bekannt, dokumentiert, auf 10.10 vorhanden, erreichbar, Token-Klasse.
/// </summary>
public sealed class ApiAvailabilityTests
{
    [Fact]
    public void Der_Index_ist_eingebettet_und_traegt_seine_Version()
    {
        Assert.Equal("10.10.0", ApiAvailability.ServerVersion);
        Assert.Equal(1137, ApiAvailability.Count);
        // GET /api/erp/v1/companies/employees/departments/ (Schraegstrich am Ende) faellt mit
        // seinem dokumentierten Geschwister zusammen; sonst ist jeder Eintrag ein Schluessel.
        Assert.Equal(1136, ApiAvailability.Operations.Count);
    }

    [Theory]
    [InlineData("GET", "/api/v1/tickets/own")]
    [InlineData("get", "/api/v1/tickets/own/")]
    [InlineData("GET", "/api/v1/tickets/own?loggedInUserId=42")]
    [InlineData("POST", "/api/v1/login")]
    public void Bekannte_Routen_der_Instanz_sind_verfuegbar_und_erreichbar(string method, string path)
    {
        Assert.True(ApiAvailability.IsKnown(method, path));
        Assert.True(ApiAvailability.IsDocumented(method, path));
        Assert.True(ApiAvailability.IsAvailableOn1010(method, path));
        Assert.True(ApiAvailability.IsReachableOn1010(method, path));
        Assert.NotEqual(ApiAvailability.DeniedTokenClass, ApiAvailability.TokenClass(method, path));
    }

    [Fact]
    public void Token_Klasse_und_Rollen_kommen_aus_dem_Index()
    {
        Assert.Equal("general", ApiAvailability.TokenClass("GET", "/api/v1/tickets/own"));
        Assert.Equal(["USER"], ApiAvailability.Roles("GET", "/api/v1/tickets/own"));
        Assert.Equal("public", ApiAvailability.TokenClass("POST", "/api/v1/login"));
        Assert.Equal("module", ApiAvailability.TokenClass("GET", "/api/tanss.x/v1/technicians"));
        Assert.Equal(["TANSS_APP"], ApiAvailability.Roles("GET", "/api/tanss.x/v1/technicians"));
    }

    [Theory]
    // Nur in der offiziellen Schnittstellenbeschreibung, auf 10.10 nicht vorhanden.
    [InlineData("DELETE", "/api/v1/currencies/{id}")]
    [InlineData("DELETE", "/api/v1/currencies/{currencyId}")]
    [InlineData("DELETE", "/api/v1/currencies/{}")]
    [InlineData("DELETE", "/api/v1/currencies/7")]
    public void Nur_in_der_Spezifikation_heisst_dokumentiert_aber_nicht_verfuegbar(string method, string path)
    {
        Assert.True(ApiAvailability.IsKnown(method, path));
        Assert.True(ApiAvailability.IsDocumented(method, path));
        Assert.False(ApiAvailability.IsAvailableOn1010(method, path));
        Assert.False(ApiAvailability.IsReachableOn1010(method, path));
    }

    [Theory]
    // Token-Klasse denied: in der Sicherheitskonfiguration von 10.10 passt keine Regel.
    [InlineData("GET", "/api/v1/sla", false)]
    [InlineData("GET", "/api/v1/bankaccounts", true)]
    public void Gesperrt_heisst_dokumentiert_aber_nicht_erreichbar(string method, string path, bool in1010)
    {
        Assert.True(ApiAvailability.IsDocumented(method, path));
        Assert.Equal(in1010, ApiAvailability.IsAvailableOn1010(method, path));
        Assert.False(ApiAvailability.IsReachableOn1010(method, path));
        Assert.Equal(ApiAvailability.DeniedTokenClass, ApiAvailability.TokenClass(method, path));
        Assert.Equal(["DENIED"], ApiAvailability.Roles(method, path));
    }

    [Theory]
    // Nur auf dem Server vorhanden, in der offiziellen Schnittstellenbeschreibung nicht gefuehrt, seit
    // der kombinierten Spezifikation aber im Index und im generierten Client.
    [InlineData("GET", "/api/v1/todos")]
    [InlineData("DELETE", "/api/v1/jwts/log/{id}")]
    [InlineData("DELETE", "/api/v1/jwts/log/4711")]
    [InlineData("GET", "/api/tanss.x/v1/technicians")]
    public void Undokumentiertes_ist_bekannt_und_erreichbar_aber_nicht_dokumentiert(string method, string path)
    {
        Assert.True(ApiAvailability.IsKnown(method, path));
        Assert.False(ApiAvailability.IsDocumented(method, path));
        Assert.True(ApiAvailability.IsAvailableOn1010(method, path));
        Assert.True(ApiAvailability.IsReachableOn1010(method, path));
        Assert.NotEmpty(ApiAvailability.Tags(method, path));
        Assert.NotNull(ApiAvailability.OperationId(method, path));
    }

    [Theory]
    [InlineData("GET", "/api/v1/doesNotExist")]
    [InlineData("PATCH", "/api/v1/tickets/own")]
    [InlineData("GET", "/api/tanss.x/v1/technicians/4711")]
    public void Unbekanntes_ist_weder_dokumentiert_noch_verfuegbar(string method, string path)
    {
        Assert.False(ApiAvailability.IsKnown(method, path));
        Assert.False(ApiAvailability.IsDocumented(method, path));
        Assert.False(ApiAvailability.IsAvailableOn1010(method, path));
        Assert.False(ApiAvailability.IsReachableOn1010(method, path));
        Assert.Null(ApiAvailability.TokenClass(method, path));
        Assert.Empty(ApiAvailability.Roles(method, path));
        Assert.Empty(ApiAvailability.Tags(method, path));
        Assert.Null(ApiAvailability.OperationId(method, path));
    }

    [Fact]
    public void Ein_konkreter_Wert_trifft_den_Platzhalter_der_woertliche_Pfad_hat_Vorrang()
    {
        Assert.Equal("get_api_v1_tickets_ticketId", ApiAvailability.OperationId("GET", "/api/v1/tickets/4711"));
        Assert.Equal("get_api_v1_tickets_own", ApiAvailability.OperationId("GET", "/api/v1/tickets/own"));
    }

    [Fact]
    public void Schlagworte_kommen_aus_der_Spezifikation()
    {
        Assert.Contains("ticket lists", ApiAvailability.Tags("GET", "/api/v1/tickets/own"));
        Assert.Contains("todos", ApiAvailability.Tags("GET", "/api/v1/todos"));
    }

    [Theory]
    [InlineData("/api/v1/tickets/{ticketId}/documents", "/api/v1/tickets/{}/documents")]
    [InlineData("/api/v1/tickets/{id}?x=1", "/api/v1/tickets/{}")]
    [InlineData("/api/v1/tickets/", "/api/v1/tickets")]
    [InlineData("/", "/")]
    public void Die_Schreibweise_wird_auf_die_des_Index_gebracht(string path, string expected)
    {
        Assert.Equal(expected, ApiAvailability.NormalisePath(path));
    }
}
