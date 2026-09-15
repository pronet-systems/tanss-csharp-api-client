// =============================================================================================
// **WARNUNG: DIESES PROJEKT DARF NIEMALS SCHREIBEND AUF EINE TANSS-INSTANZ ZUGREIFEN.** Die Tests hier
// laufen gegen eine **echte, produktive** Instanz. Erlaubt sind ausschliesslich lesende Aufrufe: GET, der
// Anmelde-POST auf /api/v1/user/login und die PUT-Routen, die in Wahrheit Abfragen sind (etwa PUT
// /api/v1/tickets mit einem Filter). Jeder Aufruf, der Daten anlegt, aendert oder loescht - POST, PUT,
// PATCH, DELETE auf Nutzdaten -, hat in diesem Projekt nichts zu suchen, auch nicht "nur zum
// Ausprobieren" und auch nicht hinter einem Schalter. Wer das aendern will, legt ein eigenes Projekt
// gegen eine Testinstanz an. Gepruefte Grundlage: die Befunde des README-Abschnitts "Gemessen, nicht
// geraten", gemessen gegen TANSS 10.10 am 2026-09-15, alle lesend.
// =============================================================================================
using System.Net;
using Microsoft.Kiota.Abstractions;
using Tanss.Api.Rest;
using Tanss.Api.Rest.Api.V1.Employees.OwnState;
using Tanss.Api.Rest.Api.V1.Todos;
using Tanss.Api.Rest.Models;
using Xunit;

namespace Tanss.Api.Live.Tests;

/// <summary>
/// Hält die Befunde des README-Abschnitts „Gemessen, nicht geraten“ gegen eine laufende Instanz fest.
/// </summary>
/// <remarks>
/// <para>Jeder Test meldet sich selbst an und gibt seinen Zugang wieder frei; die Tests sind damit
/// voneinander unabhängig. Ohne <c>TANSS_BASE_URL</c>, <c>TANSS_USER</c> und <c>TANSS_PASSWORD</c> werden
/// sie übersprungen (<see cref="LiveFactAttribute"/>).</para>
/// <para><b>Alles darin ist lesend.</b> Siehe die Warnung am Kopf der Datei.</para>
/// </remarks>
public sealed class LiveMeasurementTests
{
    [LiveFact]
    public async Task Die_Anmeldung_liefert_ein_Bearer_Token_und_eine_Mitarbeiter_Id()
    {
        // Abschnitt 1: POST /api/v1/user/login mit den Kopfzeilen user und password antwortet
        // 200; apiKey und refresh tragen beide das Praefix "Bearer ".
        (TanssApi api, TanssLoginResult login) = await LoginAsync();
        using (api)
        {
            Assert.StartsWith("Bearer ", login.ApiKey, StringComparison.Ordinal);
            Assert.StartsWith("Bearer ", login.Refresh, StringComparison.Ordinal);
            Assert.True(login.EmployeeId > 0, "employeeId muss den angemeldeten Mitarbeiter nennen.");
            Assert.True(login.ExpiresAt > DateTimeOffset.UtcNow, "Das Token muss in der Zukunft ablaufen.");

            // Auf 10.10 traegt der gemessene Weg; der dokumentierte /api/v1/login ist dort 404.
            Assert.Equal(TanssSession.LoginPath, login.LoginPath);
        }
    }

    [LiveFact]
    public async Task Ein_erfundener_Dashboard_Schluessel_meldet_LOGIN_ERROR_NO_USER_FOR_DASHBOARD()
    {
        // Abschnitt 10: POST /api/v1/user/login allein mit der Kopfzeile dbapikey nimmt im der Server den
        // Dashboard-Zweig. Ein Schluessel, den kein Mitarbeiter traegt, antwortet mit HTTP 200 und
        // content.detailMessage LOGIN_ERROR_NO_USER_FOR_DASHBOARD - genau das beweist, dass der Weg
        // bedient wird. Ein echter Schluessel ist dafuer nicht noetig; lesend ist der Aufruf ohnehin.
        using TanssApi api = TanssApi.Create(LiveEnvironment.Options(), new MutableTokenProvider());

        TanssApiException error = await Assert.ThrowsAsync<TanssApiException>(
            () => api.Session.LoginWithDashboardKeyAsync("kein-mitarbeiter-hat-diesen-schluessel-" + Guid.NewGuid()));

        Assert.Equal("LOGIN_ERROR_NO_USER_FOR_DASHBOARD", error.ErrorCode);
        Assert.Equal(TnsErrorCode.LOGIN_ERROR_NO_USER_FOR_DASHBOARD, error.KnownErrorCode);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Equal("Unsuccesful login attempt", error.Message);
    }

    [DashboardLiveFact]
    public async Task Ein_gueltiger_Dashboard_Schluessel_meldet_an()
    {
        // Abschnitt 10: Mit einem Schluessel aus TnsEmployee.dashboardApiKey kommt ein
        // gewoehnliches ACCESS-Token heraus - nur mit dem Anspruch dashboardLogin = true.
        // Laeuft nur mit TANSS_DBAPIKEY; die Schnittstelle gibt den Schluessel nicht heraus.
        using TanssApi api = TanssApi.Create(LiveEnvironment.Options(), new MutableTokenProvider());

        TanssLoginResult login = await api.Session.LoginWithDashboardKeyAsync(LiveEnvironment.DashboardKey!);

        Assert.StartsWith("Bearer ", login.ApiKey, StringComparison.Ordinal);
        Assert.True(login.EmployeeId > 0, "employeeId muss den Mitarbeiter nennen, an dem der Schluessel haengt.");
        Assert.Equal(TanssSession.LoginPath, login.LoginPath);
    }

    [LiveFact]
    public async Task Der_erzeugte_Client_erreicht_ownState()
    {
        // Abschnitt 3: apiToken mit Praefix -> 200. Abschnitt 4: ohne loggedInUserId geht es,
        // weil ein ACCESS-Token den Mitarbeiter im Claim sub traegt.
        (TanssApi api, TanssLoginResult login) = await LoginAsync();
        using (api)
        {
            OwnStateGetResponse? state = await api.Rest.Api.V1.Employees.OwnState.GetAsync();

            Assert.NotNull(state);
            Assert.NotNull(state.Content);
            Assert.Equal(login.EmployeeId, state.Content.LoggedInUser?.Id);
        }
    }

    [LiveFact]
    public async Task Ohne_das_Praefix_Bearer_antwortet_TANSS_mit_403()
    {
        // Abschnitt 3: "apiToken: <jwt>" ohne Praefix -> 403 mit leerem Koerper.
        // Die Bibliothek setzt das Praefix von sich aus (TanssApiTokenAuthenticationProvider);
        // um die Messung nachzustellen, muss es unmittelbar vor der Leitung wieder abgeschnitten
        // werden. Genau das tut StripBearerHandler.
        (TanssApi api, TanssLoginResult login) = await LoginAsync();
        using (api)
        {
            TanssApiOptions options = LiveEnvironment.Options();
            string bare = login.ApiKey["Bearer ".Length..];
            using StripBearerHandler stripped = new() { InnerHandler = TanssRestClientFactory.CreateFinalHandler(options) };
            using TanssApi ohnePraefix = TanssApi.Create(options, new StaticTokenProvider(bare), handler: stripped);

            ApiException error = await Assert.ThrowsAnyAsync<ApiException>(
                () => ohnePraefix.Rest.Api.V1.Employees.OwnState.GetAsync());

            Assert.Equal(403, error.ResponseStatusCode);
        }
    }

    [LiveFact]
    public async Task Ein_Modul_Praefix_bleibt_dem_Anmeldetoken_verschlossen()
    {
        // Abschnitt 5: Mit dem Anmeldetoken antworten alle modulgebundenen Praefixe 403 -
        // hier /api/erp/v1/customers, das ein ERP-Token verlangt (TokenRoles: ErpOrCentron).
        (TanssApi api, _) = await LoginAsync();
        using (api)
        {
            Assert.Equal(TokenRole.ErpOrCentron, TokenRoles.RequiredFor("/api/erp/v1/customers"));

            ApiException error = await Assert.ThrowsAnyAsync<ApiException>(
                () => api.Rest.Api.Erp.V1.Customers.GetAsync());

            Assert.Equal(403, error.ResponseStatusCode);
        }
    }

    [LiveFact]
    public async Task Eine_in_der_Sicherheitskonfiguration_gesperrte_Route_bleibt_gesperrt()
    {
        // Abschnitt 6: /api/v1/sla antwortet 403, obwohl die offizielle Schnittstellenbeschreibung die
        // Route beschreibt - die Kette zaehlt die USER-Module einzeln auf und endet mit denyAll.
        (TanssApi api, _) = await LoginAsync();
        using (api)
        {
            Assert.Equal(TokenRole.Denied, TokenRoles.RequiredFor("/api/v1/sla"));

            ApiException error = await Assert.ThrowsAnyAsync<ApiException>(
                () => api.Rest.Api.V1.Sla.GetAsync());

            Assert.Equal(403, error.ResponseStatusCode);
        }
    }

    [LiveFact]
    public async Task Die_undokumentierte_Route_todos_antwortet_wie_modelliert()
    {
        // Abschnitt 7: GET /api/v1/todos -> 200, content = {id, employeeId, choosenListId, lists[]} -
        // genau so setzt der Server es um (gegen 10.10 geprueft).
        (TanssApi api, TanssLoginResult login) = await LoginAsync();
        using (api)
        {
            TodosGetResponse? todos = await api.Rest.Api.V1.Todos.GetAsync();

            Assert.NotNull(todos);
            TnsTodo? todo = todos.Content;
            Assert.NotNull(todo);
            Assert.NotNull(todo.Id);
            Assert.Equal(login.EmployeeId, todo.EmployeeId);
            Assert.NotNull(todo.Lists);
        }
    }

    [LiveFact]
    public async Task Die_Erneuerung_laeuft_ueber_die_Kopfzeile_refreshToken()
    {
        // Abschnitt 2: Erneuerungstoken in refreshToken, ohne apiToken, auf einer beliebigen
        // Nicht-Anmelderoute -> 200 mit vollstaendigem Anmeldeergebnis.
        (TanssApi api, TanssLoginResult login) = await LoginAsync();
        using (api)
        {
            TanssLoginResult erneuert = await api.Session.RefreshAsync(login.Refresh);

            Assert.StartsWith("Bearer ", erneuert.ApiKey, StringComparison.Ordinal);
            Assert.StartsWith("Bearer ", erneuert.Refresh, StringComparison.Ordinal);
            Assert.Equal(login.EmployeeId, erneuert.EmployeeId);
            Assert.True(erneuert.Expire >= login.Expire, "Das erneuerte Token darf nicht frueher ablaufen.");

            // Der veraenderliche Anbieter traegt danach das neue Token; ein Aufruf damit geht.
            OwnStateGetResponse? state = await api.Rest.Api.V1.Employees.OwnState.GetAsync();
            Assert.NotNull(state?.Content);
        }
    }

    [LiveFact]
    public async Task Eine_Listenabfrage_ist_ein_PUT_und_trotzdem_lesend()
    {
        // Abschnitt 9: PUT /api/v1/tickets mit {"staff":[1],"itemsPerPage":3,"page":0}
        // antwortet 200. Das PUT ist hier die Abfrage - es legt nichts an und aendert nichts;
        // deshalb ist es der einzige Nicht-GET neben der Anmeldung, der hier stehen darf.
        (TanssApi api, TanssLoginResult login) = await LoginAsync();
        using (api)
        {
            TicketConfiguration filter = new()
            {
                Staff = [login.EmployeeId],
                ItemsPerPage = 3,
                Page = 0,
            };

            Assert.NotNull(await api.Rest.Api.V1.Tickets.PutAsync(filter));
        }
    }

    /// <summary>Meldet sich an und liefert den fertigen Zugang samt Anmeldeergebnis.</summary>
    private static async Task<(TanssApi Api, TanssLoginResult Login)> LoginAsync()
    {
        TanssApi api = TanssApi.Create(LiveEnvironment.Options(), new MutableTokenProvider());
        try
        {
            TanssLoginResult login = await api.Session.LoginAsync(LiveEnvironment.User!,
                                                                  LiveEnvironment.Password!);
            return (api, login);
        }
        catch
        {
            api.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Schneidet das Präfix <c>Bearer </c> aus der Kopfzeile <c>apiToken</c> — nur dafür da, die
    /// Ablehnung aus Abschnitt 3 nachzustellen.
    /// </summary>
    private sealed class StripBearerHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            const string Header = "apiToken";
            if (request.Headers.TryGetValues(Header, out IEnumerable<string>? values))
            {
                string bare = string.Join(",", values).Replace("Bearer ", string.Empty, StringComparison.Ordinal);
                request.Headers.Remove(Header);
                request.Headers.TryAddWithoutValidation(Header, bare);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
