using System.Net;
using System.Text.Json;

namespace Tanss.Api;

/// <summary>
/// Das Ergebnis einer Anmeldung: das Paar aus Sitzungs- und Erneuerungstoken samt Mitarbeiter und Ablauf.
/// </summary>
/// <remarks>
/// <para><b>Nachgebildet, nicht abgeleitet.</b> Die Spezifikation gibt der 200-Antwort von <c>POST
/// /api/v1/login</c> <b>kein</b> Schema, sondern nur ein Beispiel:</para>
/// <code>
/// meta: text: Welcome, your ApiToken is 4 hours valid. content: employeeId: 1 apiKey: Bearer xyzXyZ
/// expire: 1563963819 refresh: Bearer exyzXYZ employeeType: CUSTOMER
/// </code>
/// <para><b>Gemessen gegen TANSS 10.10 am 2026-09-15</b> (siehe README, Abschnitt „Gemessen, nicht
/// geraten“) antwortet eine 10.10-Instanz mit genau diesem Umschlag und <b>einem Feld mehr</b>:
/// <c>content.warning</c>. Es steht in keinem Beispiel der Spezifikation und ist hier als
/// <see cref="Warning"/> aufgenommen. Beide Token tragen das Präfix <c>Bearer </c> schon im Wert.</para>
/// <para><b>Der Statuscode taugt nicht zur Fehlererkennung</b> (gemessen gegen TANSS 10.10 am 2026-09-15,
/// siehe README, Abschnitt „Gemessen, nicht geraten“): Eine fehlgeschlagene Anmeldung antwortet ebenfalls
/// mit HTTP 200 — entweder mit <c>{"meta":{"text":"Unsuccesful login
/// attempt"},"content":{"detailMessage":"LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD"}}</c> oder, bei einer
/// Anfrage ohne die erwarteten Kopfzeilen, mit einem <b>leeren</b> Rumpf. Maßgeblich ist deshalb allein,
/// ob <c>content.apiKey</c> vorhanden ist; <see cref="Parse(string)"/> wirft sonst eine
/// <see cref="TanssApiException"/> mit dem Code aus <c>content.detailMessage</c> beziehungsweise
/// <see cref="EmptyResponseCode"/>.</para>
/// <para><b>Die Token tragen ihr Präfix schon.</b> Laut Beschreibung „The <c>apiKey</c> value already
/// includes the literal <c>Bearer </c> prefix — send it verbatim under the <c>apiToken</c> header“;
/// gemessen ist dasselbe (gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen,
/// nicht geraten“). <see cref="ApiKey"/> wird deshalb unverändert weitergereicht.</para>
/// </remarks>
/// <param name="EmployeeId">Die Mitarbeiter-Id aus <c>content.employeeId</c> — der Wert für
/// <c>loggedInUserId</c>.</param>
/// <param name="ApiKey">Das Sitzungstoken aus <c>content.apiKey</c>, einschließlich <c>Bearer </c>; vier
/// Stunden gültig.</param>
/// <param name="Expire">Der Ablauf aus <c>content.expire</c> als Unix-Zeit in Sekunden.</param>
/// <param name="Refresh">Das Erneuerungstoken aus <c>content.refresh</c>; fünf Tage gültig.</param>
/// <param name="EmployeeType">Die Art des Kontos aus <c>content.employeeType</c>, gemessen
/// <c>COMPANY_ADMIN</c>.</param>
/// <param name="Warning">Der Warnhinweis aus <c>content.warning</c> — gemessen an einer 10.10-Instanz
/// (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“), in der
/// Spezifikation nicht beschrieben; <c>null</c>, wenn die Instanz keinen mitschickt.</param>
/// <param name="LoginPath">Der Pfad, über den diese Anmeldung zustande kam:
/// <see cref="TanssSession.LoginPath"/> (<c>/api/v1/user/login</c>, der gemessene Weg mit Kopfzeilen)
/// oder <see cref="TanssSession.DocumentedLoginPath"/> (<c>/api/v1/login</c>, der dokumentierte Weg mit
/// JSON-Rumpf, auf den die Sitzung nach einer 404 ausweicht). <see cref="Parse(string)"/> lässt das Feld
/// <c>null</c>; gesetzt wird es von <see cref="TanssSession.LoginAsync"/>. Nach einer Erneuerung bleibt
/// es <c>null</c>.</param>
public sealed record TanssLoginResult(int EmployeeId, string ApiKey, long Expire, string Refresh,
                                      string? EmployeeType, string? Warning = null,
                                      string? LoginPath = null)
{
    /// <summary>
    /// Der Code, unter dem eine Anmeldung mit <b>leerem</b> Rumpf gemeldet wird: TANSS 10.10 beantwortet
    /// eine Anmeldeanfrage ohne die erwarteten Kopfzeilen mit HTTP 200 und ohne jeden Inhalt (gemessen
    /// gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“). Der Wert
    /// stammt nicht von TANSS, sondern ist die Kennung dieser Bibliothek für genau diesen Fall.
    /// </summary>
    public const string EmptyResponseCode = "LOGIN_EMPTY_RESPONSE";

    /// <summary>Der Ablauf als Zeitpunkt, abgeleitet aus <see cref="Expire"/> (Unix-Sekunden).</summary>
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeSeconds(Expire);

    /// <summary>
    /// Liest die Antwort einer Anmeldung oder Erneuerung — den Umschlag <c>{ meta, content }</c>.
    /// </summary>
    /// <remarks>
    /// <b>Erfolg heißt: <c>content.apiKey</c> ist da.</b> Der Statuscode sagt nichts (gemessen gegen
    /// TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“); fehlt der
    /// Schlüssel, wirft diese Methode eine <see cref="TanssApiException"/>, deren
    /// <see cref="TanssApiException.ErrorCode"/> der gemessene Wert aus <c>content.detailMessage</c> ist
    /// (etwa <c>LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD</c>) und deren Meldung <c>meta.text</c> trägt.
    /// Ist der Rumpf leer, ist der Code <see cref="EmptyResponseCode"/>.
    /// </remarks>
    /// <param name="json">Der Rumpf der Antwort.</param>
    /// <exception cref="TanssApiException">Der Rumpf ist leer, kein JSON-Objekt oder trägt kein
    /// <c>content.apiKey</c>.</exception>
    public static TanssLoginResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw Empty(json);
        }

        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException cause)
        {
            throw new TanssApiException(
                "Die Antwort auf die Anmeldung war kein JSON. Erwartet wird der Umschlag "
                + "{ meta, content } mit apiKey, refresh, expire und employeeId.", cause);
        }

        JsonElement content = root.ValueKind == JsonValueKind.Object
                              && root.TryGetProperty("content", out JsonElement found)
                              && found.ValueKind == JsonValueKind.Object
            ? found
            : default;

        if (TextOf(content, "apiKey") is not { Length: > 0 } apiKey)
        {
            throw Rejected(json, root, content);
        }

        return new TanssLoginResult(
            (int)NumberOf(content, "employeeId"),
            apiKey,
            NumberOf(content, "expire"),
            TextOf(content, "refresh") ?? string.Empty,
            TextOf(content, "employeeType"),
            TextOf(content, "warning"));
    }

    private static TanssApiException Empty(string json) =>
        new("Die Anmeldung kam mit leerem Rumpf und HTTP 200 zurück. So beantwortet TANSS 10.10 "
            + "eine Anmeldeanfrage, die die Zugangsdaten nicht in den Kopfzeilen user/password "
            + "beziehungsweise user/logintoken trägt (gemessen gegen TANSS 10.10 am "
            + "2026-09-15).",
            HttpStatusCode.OK, EmptyResponseCode, localizedText: null, traceId: null, rawBody: json);

    private static TanssApiException Rejected(string json, JsonElement root, JsonElement content)
    {
        string? code = TextOf(content, "detailMessage");
        string? meta = root.ValueKind == JsonValueKind.Object
                       && root.TryGetProperty("meta", out JsonElement found)
                       && found.ValueKind == JsonValueKind.Object
            ? TextOf(found, "text")
            : null;

        string message = meta is { Length: > 0 }
            ? meta
            : "Die Antwort auf die Anmeldung trug kein content.apiKey und damit kein Token "
              + "(gemessen gegen TANSS 10.10 am 2026-09-15).";

        return new TanssApiException(message, HttpStatusCode.OK, code, meta, traceId: null, rawBody: json);
    }

    private static string? TextOf(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object
        && owner.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long NumberOf(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object
        && owner.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
            ? number
            : 0L;
}
