using System.Net;
using Tanss.Api.Rest;
using Tanss.Api.Rest.Api.TanssX.V1.Technicians;
using Tanss.Api.Rest.Api.V1.Todos;
using Tanss.Api.Tests.Fakes;
using Xunit;

namespace Tanss.Api.Tests.Rest;

/// <summary>
/// Der generierte Client kennt auch die 295 Routen, die die offizielle Schnittstellenbeschreibung nicht
/// fuehrt (vom Server so umgesetzt, gegen 10.10 geprueft): die Builder sind da, und die Regeln der
/// Anbindung gelten fuer sie genauso.
/// </summary>
public sealed class UndocumentedRoutesTests
{
    [Fact]
    public void Die_Builder_der_undokumentierten_Routen_sind_erzeugt()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);

        // Ein Ausschnitt quer durch die Praefixe; verschwindet eine Route aus der kombinierten
        // Spezifikation, bricht hier der Bau.
        Assert.NotNull(client.Api.V1.Todos);                       // GET/POST /api/v1/todos
        Assert.NotNull(client.Api.V1.Todos[7]);                    // PUT/DELETE /api/v1/todos/{id}
        Assert.NotNull(client.Api.V1.Jwts.Log[7]);                 // DELETE /api/v1/jwts/log/{id}
        Assert.NotNull(client.Api.V1.Ai.TanssAI);                  // POST /api/v1/ai/tanssAI/...
        Assert.NotNull(client.Api.V1.ManagementDashboard);         // /api/v1/managementDashboard/...
        Assert.NotNull(client.Api.V1.Push);                        // /api/v1/push/config/...
        Assert.NotNull(client.Api.TanssX.V1.Technicians);          // GET /api/tanss.x/v1/technicians
        Assert.NotNull(client.Api.TanssX.V1.Ticket);               // /api/tanss.x/v1/ticket/...
        Assert.NotNull(client.Api.TanssApp.V1.Supports);           // /api/tanss.app/v1/supports/...
        Assert.NotNull(client.Api.Coero.V1.Timestamp);             // POST /api/coero/v1/timestamp
        Assert.NotNull(client.Api.Systemhaus_one.V1.Tickets);      // /api/systemhaus_one/v1/tickets/...
        Assert.NotNull(client.Api.DeviceManagement.V1.Pcs);        // /api/deviceManagement/v1/pcs/...
        Assert.NotNull(client.WellKnown.JwksJson);                 // GET /.well-known/jwks.json
    }

    [Fact]
    public async Task Todos_gehen_mit_loggedInUserId_und_apiToken()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);

        TodosGetResponse? response = await client.Api.V1.Todos.GetAsync();

        Assert.NotNull(response);
        Assert.Equal("GET", handler.Last.Method);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/v1/todos?loggedInUserId=42", handler.Last.Uri.ToString());
        Assert.Equal("Bearer test.token.value", handler.Last.ApiToken);
    }

    [Fact]
    public async Task Technicians_auf_tanss_x_gehen_ohne_loggedInUserId()
    {
        using RecordingHandler handler = new(HttpStatusCode.OK, TestEnvironment.EmptyList);
        TanssRestClient client = TanssRestClientFactory.Create(
            TestEnvironment.Options(), new FakeTokenProvider(TestEnvironment.Token), handler: handler);

        TechniciansGetResponse? response = await client.Api.TanssX.V1.Technicians.GetAsync();

        Assert.NotNull(response);
        Assert.Equal(TestEnvironment.BaseUrl + "/api/tanss.x/v1/technicians", handler.Last.Uri.ToString());
        Assert.Null(handler.Last.Query("loggedInUserId"));
        Assert.Equal("Bearer test.token.value", handler.Last.ApiToken);
    }
}
