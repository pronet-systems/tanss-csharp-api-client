using Xunit;

namespace Tanss.Api.Tests;

/// <summary>Die Präfix-Tabelle des Servers (gegen TANSS 10.10 geprüft), Zeile für Zeile.</summary>
public sealed class TokenRolesTests
{
    [Theory]
    [InlineData("/api/v1/tickets/own", TokenRole.User)]
    [InlineData("/api/v1/tickets/own?loggedInUserId=42", TokenRole.User)]
    [InlineData("/api/v1/timers/", TokenRole.User)]
    [InlineData("/api/erp/v1/customers", TokenRole.ErpOrCentron)]
    [InlineData("/api/tanss.x/v1/supports", TokenRole.TanssApp)]
    [InlineData("/api/tanss.app/v1/supports", TokenRole.TanssApp)]
    [InlineData("/api/v1/login", TokenRole.None)]
    [InlineData("/api/v1/login/", TokenRole.None)]
    [InlineData("/foo", TokenRole.Denied)]
    [InlineData("/api/v1", TokenRole.Denied)]
    [InlineData("/api/v2/tickets", TokenRole.Denied)]
    [InlineData("/api/systemhaus_one/v1/x", TokenRole.SystemhausOne)]
    [InlineData("/api/coero/v1", TokenRole.Coero)]
    [InlineData("/api/calls/v1/calls", TokenRole.Phone)]
    [InlineData("/api/remoteSupports/v1/remoteSupports", TokenRole.RemoteSupport)]
    [InlineData("/api/monitoring/v1/x", TokenRole.Monitoring)]
    [InlineData("/api/timestamps/v1/x", TokenRole.Timestamp)]
    [InlineData("/api/deviceManagement/v1/x", TokenRole.DeviceManagement)]
    [InlineData("/api/servereye/v1/x", TokenRole.Servereye)]
    // Sonderzeilen unter /api/v1 stehen vor der Sammelregel.
    [InlineData("/api/v1/offers/17", TokenRole.UserOrOffer)]
    [InlineData("/api/v1/cache", TokenRole.UserOrPhp)]
    [InlineData("/api/v1/util/languages", TokenRole.UserOrLandingPage)]
    [InlineData("/api/v1/util/files/abc", TokenRole.None)]
    [InlineData("/api/v1/util/other", TokenRole.User)]
    [InlineData("/api/v1/landingPage/ticketWorkflow/x", TokenRole.LandingPageTicketWorkflow)]
    [InlineData("/api/v1/landingPage/contractWorkflow", TokenRole.LandingPageContractWorkflow)]
    [InlineData("/actuator/health", TokenRole.Actuator)]
    [InlineData("/api/v1/cloud/isTokenValid/x", TokenRole.None)]
    [InlineData("/api/v1/cloud/other", TokenRole.User)]
    [InlineData("/api/v1/starface/inc/5/abc", TokenRole.None)]
    [InlineData("/api/v1/starface/inc/5", TokenRole.User)]
    [InlineData("/.well-known/jwks.json", TokenRole.None)]
    // Der Praefixvergleich ist segmentweise: /api/v1/loginX ist nicht /api/v1/login.
    [InlineData("/api/v1/loginX", TokenRole.Denied)] // keine Regel trifft: der Server kennt nur exakt /api/v1/login
    [InlineData("/api/erp/v1x/customers", TokenRole.Denied)]
    // Eine vollstaendige Adresse wird ab dem ersten /api/ gelesen.
    [InlineData("https://tanss.example.invalid/backend/api/erp/v1/customers", TokenRole.ErpOrCentron)]
    public void Die_Rolle_folgt_der_ersten_passenden_Zeile(string path, TokenRole expected)
    {
        Assert.Equal(expected, TokenRoles.RequiredFor(path));
    }

    [Theory]
    [InlineData("/api/v1/tickets/own", true)]
    [InlineData("/api/v1/login", true)]
    [InlineData("/api/v1/offers/1", true)]
    [InlineData("/api/erp/v1/customers", false)]
    [InlineData("/api/tanss.x/v1/supports", false)]
    [InlineData("/api/remoteSupports/v1/remoteSupports", false)]
    [InlineData("/foo", false)]
    public void Das_Login_Token_oeffnet_nur_die_USER_Praefixe(string path, bool expected)
    {
        Assert.Equal(expected, TokenRoles.IsReachableWithLoginToken(path));
    }

    [Theory]
    // Die Bedingung, unter der loggedInUserId angehaengt wird (vom Server so umgesetzt, gegen 10.10
    // geprueft).
    [InlineData(TokenRole.User, true)]
    [InlineData(TokenRole.UserOrOffer, true)]
    [InlineData(TokenRole.UserOrPhp, true)]
    [InlineData(TokenRole.UserOrLandingPage, true)]
    [InlineData(TokenRole.None, false)]
    [InlineData(TokenRole.Denied, false)]
    [InlineData(TokenRole.TanssApp, false)]
    [InlineData(TokenRole.ErpOrCentron, false)]
    [InlineData(TokenRole.RemoteSupport, false)]
    [InlineData(TokenRole.Actuator, false)]
    public void USER_steckt_nur_in_den_USER_Rollen(TokenRole role, bool expected)
    {
        Assert.Equal(expected, TokenRoles.IncludesUser(role));
    }
}
