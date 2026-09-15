using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Tanss.Api.Rest.Models;

namespace Tanss.Api;

/// <summary>
/// Eine Antwort, die TANSS als Fehler ausgewiesen hat — in einer der beiden Formen, die eine
/// 10.10-Instanz gemessen liefert.
/// </summary>
/// <remarks>
/// <para><b>Der fachliche Umschlag.</b> Die Spezifikation kennt für Fehler die wiederverwendete Antwort
/// <c>components.responses.ErrorResponse</c> („error response“): ein Objekt mit der einen Eigenschaft
/// <c>error</c> vom Typ <c>components.schemas.TnsException</c> („describes an error that occurs in
/// TANSS“). <c>TnsException</c> hat dort vier Felder: <c>text</c>, <c>localizedText</c>,
/// <c>thrownExceptionMessage</c> und <c>type</c>. <b>Gemessen</b> kommt ein fünftes hinzu: <c>traceId</c>
/// (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“).</para>
/// <para><b>Der Code steckt in <c>text</c>.</b> Dessen Beschreibung sagt es wörtlich: „Stable error code
/// (see object). For example <c>CANT_CHECK_ITEM_TWICE</c>. Use this (not <c>localizedText</c>) for
/// programmatic error handling.“ Der Wertevorrat ist die Aufzählung
/// <c>components.schemas.TnsErrorCode</c>, im erzeugten Client als <see cref="TnsErrorCode"/> vorhanden;
/// <see cref="KnownErrorCode"/> bildet die Zeichenkette darauf ab.</para>
/// <para><b>Die zweite Form: RFC 7807.</b> Fehler, die schon Spring abweist — etwa ein Pfadparameter vom
/// falschen Typ —, kommen gar nicht im TANSS-Umschlag, sondern als „problem detail“-Dokument <c>{type,
/// title, status, detail, instance}</c> (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README,
/// Abschnitt „Gemessen, nicht geraten“). Ein Client muss beide lesen können. Diese Klasse hält es so:
/// <see cref="ErrorCode"/> bleibt dabei <b>null</b> — <c>title</c> („Bad Request“) ist kein stabiler
/// TANSS-Code und darf nicht als einer durchgehen —, der <c>title</c> steht in <see cref="ProblemTitle"/>
/// und macht die Form erkennbar, <c>detail</c> steht in <see cref="LocalizedText"/>, beide zusammen in
/// <see cref="Exception.Message"/>, und <c>type</c>, <c>status</c> und <c>instance</c> stehen unverändert
/// im <see cref="RawBody"/>.</para>
/// <para><b>Zwei weitere dokumentierte Formen</b> werden mitgelesen: das Schema
/// <c>TnsMetaMessage-Error</c> mit dem Feld <c>text</c> („error message goes here“), das etwa die
/// 403-Antwort der Tagesabschlüsse als <c>meta</c> trägt, sowie <c>content.detailMessage</c> der
/// abgelehnten Anmeldung (<c>LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD</c>), die gemessen mit HTTP 200
/// kommt (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht
/// geraten“).</para>
/// <para><b>Und die dritte Form ist gar keine.</b> Jede Ablehnung — fehlendes Token, fehlendes Präfix
/// <c>Bearer </c>, <c>Authorization</c> statt <c>apiToken</c>, ein Modul-Präfix ohne dessen Rolle, eine
/// von der Sicherheitskonfiguration des Servers gesperrte Route — kommt mit <b>leerem Rumpf</b> (gemessen
/// gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“). Dafür gibt es
/// nichts zu lesen; <see cref="FromResponse"/> schreibt den Grund in die Meldung.</para>
/// </remarks>
public class TanssApiException : Exception
{
    /// <summary>Baut die Ausnahme mit der Vorgabemeldung.</summary>
    public TanssApiException()
        : this("TANSS hat die Anfrage mit einem Fehler beantwortet.")
    {
    }

    /// <summary>Baut die Ausnahme mit einer Meldung.</summary>
    /// <param name="message">Der Text für den Aufrufer.</param>
    public TanssApiException(string message)
        : base(message)
    {
    }

    /// <summary>Baut die Ausnahme mit einer Meldung und einer Ursache.</summary>
    /// <param name="message">Der Text für den Aufrufer.</param>
    /// <param name="innerException">Die auslösende Ausnahme.</param>
    public TanssApiException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Baut die Ausnahme aus dem, was die Antwort hergab.</summary>
    /// <param name="message">Der Text für den Aufrufer.</param>
    /// <param name="statusCode">Der HTTP-Status der Antwort.</param>
    /// <param name="errorCode">Der maschinenlesbare Code aus <c>error.text</c>.</param>
    /// <param name="localizedText">
    /// Der lesbare Text aus <c>error.localizedText</c>, <c>meta.text</c> oder — bei einem
    /// RFC-7807-Dokument — <c>detail</c>.
    ///</param>
    /// <param name="traceId">Die Ablaufkennung aus <c>error.traceId</c>.</param>
    /// <param name="rawBody">Der Rumpf der Antwort, unverändert.</param>
    /// <param name="problemTitle">Der <c>title</c> eines RFC-7807-Dokuments; sonst <c>null</c>.</param>
    public TanssApiException(string message, HttpStatusCode statusCode, string? errorCode = null,
                             string? localizedText = null, string? traceId = null, string? rawBody = null,
                             string? problemTitle = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        LocalizedText = localizedText;
        TraceId = traceId;
        RawBody = rawBody;
        ProblemTitle = problemTitle;
    }

    /// <summary>Der HTTP-Status der Antwort, etwa <see cref="HttpStatusCode.Forbidden"/>.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Der stabile, maschinenlesbare Code aus <c>error.text</c> — der Wert, nach dem ein
    /// Aufrufer verzweigen soll (Spezifikation, Schema <c>TnsException.text</c>). Bei einem
    /// RFC-7807-Dokument bleibt er <c>null</c>: Dessen <c>title</c> ist Springs Fassung des
    /// Statuscodes, kein TANSS-Code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Der lesbare Text: <c>error.localizedText</c> („localized error message (currently only in
    /// english)“), ersatzweise <c>meta.text</c> oder das <c>detail</c> eines
    /// RFC-7807-Dokuments. Nichts, worauf Code sich stützen sollte.
    /// </summary>
    public string? LocalizedText { get; }

    /// <summary>Die Ablaufkennung aus <c>error.traceId</c>.</summary>
    /// <remarks>
    /// Die Spezifikation beschreibt dieses Feld nicht — eine 10.10-Instanz schickt es trotzdem in jedem
    /// fachlichen Fehlerumschlag mit, gemessen etwa
    /// <c>"traceId":"f1e68760-2060-4797-bd25-a7b33b5fe0a6"</c> (gemessen gegen TANSS 10.10 am 2026-09-15,
    /// siehe README, Abschnitt „Gemessen, nicht geraten“). Es ist der Wert, mit dem der Vorgang im
    /// Protokoll der Instanz wiederzufinden ist. <c>null</c> bleibt es bei Formen, die keine Kennung
    /// tragen — RFC-7807-Dokument, leerer Rumpf.
    /// </remarks>
    public string? TraceId { get; }

    /// <summary>
    /// Der <c>title</c> eines RFC-7807-Dokuments, etwa <c>Bad Request</c>; <c>null</c> bei jeder anderen
    /// Form. Er ist zugleich das Kennzeichen dieser Form (gemessen gegen TANSS 10.10 am 2026-09-15, siehe
    /// README, Abschnitt „Gemessen, nicht geraten“): Ist er gesetzt, kam der Fehler von Spring und nicht
    /// aus TANSS, und <see cref="ErrorCode"/> ist dann <c>null</c>.
    /// </summary>
    public string? ProblemTitle { get; }

    /// <summary>Der Rumpf der Antwort, unverändert — für Protokoll und Fehlersuche.</summary>
    public string? RawBody { get; }

    /// <summary>
    /// Der Code als Wert der erzeugten Aufzählung <see cref="TnsErrorCode"/>; <c>null</c>, wenn
    /// <see cref="ErrorCode"/> leer ist oder die offizielle Schnittstellenbeschreibung ihn nicht kennt.
    /// </summary>
    public TnsErrorCode? KnownErrorCode =>
        Enum.TryParse(ErrorCode, ignoreCase: false, out TnsErrorCode parsed) ? parsed : null;

    /// <summary>
    /// Liest den Fehlerrumpf. Liefert <c>true</c>, wenn er eine der beschriebenen oder
    /// gemessenen Formen trägt.
    /// </summary>
    /// <param name="statusCode">Der HTTP-Status der Antwort.</param>
    /// <param name="body">Der Rumpf der Antwort.</param>
    /// <param name="error">Die gebaute Ausnahme; <c>null</c>, wenn nichts Bekanntes darin stand.</param>
    /// <remarks>
    /// Gelesen werden <c>error.text</c>, <c>error.localizedText</c>, <c>error.traceId</c>
    /// (gemessen, Abschnitt 8), <c>meta.text</c>, <c>content.detailMessage</c> und die Felder
    /// <c>title</c>/<c>detail</c> des RFC-7807-Dokuments (gemessen, Abschnitt 8) — und sonst
    /// nichts. Ist der Rumpf kein JSON-Objekt oder enthält er keines dieser Felder, bleibt
    /// <paramref name="error"/> <c>null</c>; der Aufrufer nimmt dann
    /// <see cref="FromResponse"/>, das auch aus einem leeren oder fremden Rumpf eine Ausnahme
    /// mit Status und <see cref="RawBody"/> macht.
    /// </remarks>
    public static bool TryParse(HttpStatusCode statusCode, string? body,
                                [NotNullWhen(true)] out TanssApiException? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        string? errorCode = null;
        string? localizedText = null;
        string? traceId = null;
        string? problemTitle = null;
        string? problemDetail = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // components.responses.ErrorResponse -> { "error": TnsException }, gemessen samt traceId.
            if (document.RootElement.TryGetProperty("error", out JsonElement thrown)
                && thrown.ValueKind == JsonValueKind.Object)
            {
                errorCode = Text(thrown, "text");
                localizedText = Text(thrown, "localizedText");
                traceId = Text(thrown, "traceId");
            }

            // Schema TnsMetaMessage-Error: { "meta": { "text": "..." } }
            if (document.RootElement.TryGetProperty("meta", out JsonElement meta)
                && meta.ValueKind == JsonValueKind.Object)
            {
                localizedText ??= Text(meta, "text");
            }

            // Abgelehnte Anmeldung: { "content": { "detailMessage": "..." } }
            if (document.RootElement.TryGetProperty("content", out JsonElement content)
                && content.ValueKind == JsonValueKind.Object)
            {
                errorCode ??= Text(content, "detailMessage");
            }

            traceId ??= Text(document.RootElement, "traceId");

            // RFC 7807: { "type", "title", "status", "detail", "instance" } - nur dann, wenn der
            // TANSS-Umschlag nichts hergab; sonst gewinnt der fachliche Code.
            if (errorCode is null && localizedText is null)
            {
                problemTitle = Text(document.RootElement, "title");
                problemDetail = Text(document.RootElement, "detail");
            }
        }
        catch (JsonException)
        {
            // Kein JSON: dann steht auch nichts Bekanntes darin.
            return false;
        }

        if (problemTitle is not null || problemDetail is not null)
        {
            error = new TanssApiException(ProblemMessage(statusCode, problemTitle, problemDetail),
                                          statusCode, errorCode: null, problemDetail ?? problemTitle,
                                          traceId, body, problemTitle);
            return true;
        }

        if (errorCode is null && localizedText is null)
        {
            return false;
        }

        error = new TanssApiException(MessageFor(statusCode, errorCode, localizedText),
                                      statusCode, errorCode, localizedText, traceId, body);
        return true;
    }

    /// <summary>
    /// Die Ausnahme zu einer Fehlerantwort — mit den gelesenen Feldern, wenn sie da sind, und sonst mit
    /// Status und Rumpf.
    /// </summary>
    /// <remarks>
    /// Ist der Rumpf leer, nennt die Meldung den Grund: Eine 10.10-Instanz beantwortet jede Ablehnung
    /// genau so — ohne einen einzigen Hinweis im Rumpf (gemessen gegen TANSS 10.10 am 2026-09-15, siehe
    /// README, Abschnitt „Gemessen, nicht geraten“).
    /// </remarks>
    /// <param name="statusCode">Der HTTP-Status der Antwort.</param>
    /// <param name="body">Der Rumpf der Antwort.</param>
    public static TanssApiException FromResponse(HttpStatusCode statusCode, string? body) =>
        TryParse(statusCode, body, out TanssApiException? parsed)
            ? parsed
            : new TanssApiException(EmptyOrForeign(statusCode, body),
                                    statusCode, errorCode: null, localizedText: null, traceId: null,
                                    rawBody: body);

    private static string EmptyOrForeign(HttpStatusCode statusCode, string? body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            return MessageFor(statusCode, errorCode: null, localizedText: null);
        }

        string status = $"TANSS antwortete mit HTTP {(int)statusCode} ({statusCode}) und leerem Rumpf";
        return statusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            ? status + ". TANSS beantwortet Ablehnungen mit einem leeren Rumpf: Der Grund steht "
                     + "nirgends in der Antwort. Gemessen kommt diese Form bei fehlendem Token, "
                     + "bei einem Token ohne das Präfix „Bearer “, bei „Authorization“ statt "
                     + "„apiToken“, bei einem Modul-Präfix ohne dessen Rolle und bei einer Route, "
                     + "die Sicherheitskonfiguration des Servers sperrt (gemessen gegen TANSS "
                     + "10.10 am 2026-09-15)."
            : status + ".";
    }

    private static string ProblemMessage(HttpStatusCode statusCode, string? title, string? detail)
    {
        string status = $"TANSS antwortete mit HTTP {(int)statusCode} ({statusCode})";
        return (title, detail) switch
        {
            (not null, not null) => $"{status}: {title} — {detail}",
            (not null, null) => $"{status}: {title}",
            (null, not null) => $"{status}: {detail}",
            _ => status + ".",
        };
    }

    private static string MessageFor(HttpStatusCode statusCode, string? errorCode, string? localizedText)
    {
        string status = $"TANSS antwortete mit HTTP {(int)statusCode} ({statusCode})";
        return (errorCode, localizedText) switch
        {
            (not null, not null) => $"{status}: {errorCode} — {localizedText}",
            (not null, null) => $"{status}: {errorCode}",
            (null, not null) => $"{status}: {localizedText}",
            _ => status + ".",
        };
    }

    private static string? Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
