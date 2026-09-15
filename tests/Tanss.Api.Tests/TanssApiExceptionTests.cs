using System.Net;
using Tanss.Api.Rest.Models;
using Xunit;

namespace Tanss.Api.Tests;

/// <summary>
/// Der dokumentierte Fehlerumschlag: <c>components.responses.ErrorResponse</c> mit
/// <c>TnsException</c> darin.
/// </summary>
public sealed class TanssApiExceptionTests
{
    [Fact]
    public void Eine_403_im_dokumentierten_Umschlag_wird_zu_Code_und_Text()
    {
        // Das Beispiel aus components.responses.ErrorResponse, woertlich.
        const string Body = """
            {"error":{"text":"CANT_CHECK_ITEM_TWICE",
                      "localizedText":"Item is checked and can't be checked a second time",
                      "type":"InvalidArgumentException"}}
            """;

        Assert.True(TanssApiException.TryParse(HttpStatusCode.Forbidden, Body, out TanssApiException? error));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("CANT_CHECK_ITEM_TWICE", error.ErrorCode);
        Assert.Equal("Item is checked and can't be checked a second time", error.LocalizedText);
        Assert.Equal(Body, error.RawBody);
        // Die Spezifikation beschreibt kein traceId-Feld; dieses Beispiel traegt keines.
        Assert.Null(error.TraceId);
        Assert.Null(error.ProblemTitle);
        // Der Code steht als Wert der erzeugten Aufzaehlung TnsErrorCode zur Verfuegung.
        Assert.Equal(TnsErrorCode.CANT_CHECK_ITEM_TWICE, error.KnownErrorCode);
        Assert.Contains("CANT_CHECK_ITEM_TWICE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_unbekannter_Code_bleibt_als_Zeichenkette_erhalten()
    {
        Assert.True(TanssApiException.TryParse(HttpStatusCode.BadRequest,
                                               """{"error":{"text":"EIN_NEUER_CODE"}}""",
                                               out TanssApiException? error));
        Assert.Equal("EIN_NEUER_CODE", error.ErrorCode);
        Assert.Null(error.KnownErrorCode);
        Assert.Null(error.LocalizedText);
    }

    [Fact]
    public void Das_Schema_TnsMetaMessage_Error_traegt_seinen_Text_in_meta()
    {
        Assert.True(TanssApiException.TryParse(HttpStatusCode.Forbidden,
                                               """{"meta":{"text":"day closing already exists"}}""",
                                               out TanssApiException? error));
        Assert.Null(error.ErrorCode);
        Assert.Equal("day closing already exists", error.LocalizedText);
    }

    [Fact]
    public void Die_403_der_Anmeldung_traegt_ihren_Code_in_content_detailMessage()
    {
        const string Body = """
            {"meta":{"text":"Unsuccessful login attempt"},
             "content":{"detailMessage":"LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD"}}
            """;

        Assert.True(TanssApiException.TryParse(HttpStatusCode.Forbidden, Body, out TanssApiException? error));
        Assert.Equal("LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD", error.ErrorCode);
        Assert.Equal("Unsuccessful login attempt", error.LocalizedText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>400 Bad Request</html>")]
    [InlineData("""{"content":[]}""")]
    [InlineData("[]")]
    public void Was_nicht_dokumentiert_ist_wird_nicht_geraten(string? body)
    {
        Assert.False(TanssApiException.TryParse(HttpStatusCode.BadRequest, body, out TanssApiException? error));
        Assert.Null(error);
    }

    [Fact]
    public void FromResponse_liefert_auch_ohne_Umschlag_Status_und_Rumpf()
    {
        TanssApiException error = TanssApiException.FromResponse(HttpStatusCode.BadGateway, "<html>502</html>");

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Null(error.ErrorCode);
        Assert.Equal("<html>502</html>", error.RawBody);
        Assert.Contains("502", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Der_gemessene_Umschlag_traegt_eine_traceId()
    {
        // Gemessen gegen TANSS 10.10 am 2026-09-15: die Huelle kommt MIT traceId, obwohl die
        // Spezifikation dieses Feld nicht kennt.
        const string Body = """
            {"error":{"text":"FORBIDDEN_MISSING_PERMISSIONS",
                      "localizedText":"Sie haben nicht die Berechtigung, diese Aktion auszufuehren",
                      "type":"ForbiddenException",
                      "traceId":"f1e68760-2060-4797-bd25-a7b33b5fe0a6"}}
            """;

        Assert.True(TanssApiException.TryParse(HttpStatusCode.Forbidden, Body, out TanssApiException? error));
        Assert.Equal("FORBIDDEN_MISSING_PERMISSIONS", error.ErrorCode);
        Assert.Equal("Sie haben nicht die Berechtigung, diese Aktion auszufuehren", error.LocalizedText);
        Assert.Equal("f1e68760-2060-4797-bd25-a7b33b5fe0a6", error.TraceId);
        Assert.Null(error.ProblemTitle);
    }

    [Fact]
    public void Das_RFC_7807_Dokument_von_Spring_wird_ebenfalls_gelesen()
    {
        // Gemessen gegen TANSS 10.10 am 2026-09-15: Was schon Spring abweist, kommt nicht im
        // TANSS-Umschlag, sondern als "problem detail"-Dokument.
        const string Body = """
            {"type":"about:blank","title":"Bad Request","status":400,
             "detail":"Failed to convert 'type' with value: 'abc'","instance":"/api/v1/ai/prompts/abc"}
            """;

        Assert.True(TanssApiException.TryParse(HttpStatusCode.BadRequest, Body, out TanssApiException? error));
        // title ist Springs Fassung des Statuscodes, kein stabiler TANSS-Code.
        Assert.Null(error.ErrorCode);
        Assert.Null(error.KnownErrorCode);
        Assert.Equal("Bad Request", error.ProblemTitle);
        Assert.Equal("Failed to convert 'type' with value: 'abc'", error.LocalizedText);
        Assert.Contains("Bad Request", error.Message, StringComparison.Ordinal);
        Assert.Contains("Failed to convert", error.Message, StringComparison.Ordinal);
        Assert.Equal(Body, error.RawBody);
    }

    [Fact]
    public void Der_fachliche_Code_geht_dem_RFC_7807_Titel_vor()
    {
        Assert.True(TanssApiException.TryParse(HttpStatusCode.NotFound,
                                               """{"error":{"text":"OBJECT_NOT_FOUND","type":"DataNotFoundException"},"title":"Not Found"}""",
                                               out TanssApiException? error));
        Assert.Equal("OBJECT_NOT_FOUND", error.ErrorCode);
        Assert.Null(error.ProblemTitle);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void Eine_Ablehnung_mit_leerem_Rumpf_sagt_wenigstens_warum_nichts_dasteht(HttpStatusCode status)
    {
        // Gemessen gegen TANSS 10.10 am 2026-09-15: Alle Ablehnungen kommen mit leerem Koerper.
        TanssApiException error = TanssApiException.FromResponse(status, string.Empty);

        Assert.Equal(status, error.StatusCode);
        Assert.Null(error.ErrorCode);
        Assert.Contains("leerem Rumpf", error.Message, StringComparison.Ordinal);
        Assert.Contains("gemessen gegen TANSS 10.10", error.Message, StringComparison.Ordinal);
        Assert.Contains("Bearer", error.Message, StringComparison.Ordinal);
    }
}
