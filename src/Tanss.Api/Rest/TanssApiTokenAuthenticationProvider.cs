using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Tanss.Api.Rest;

/// <summary>
/// Setzt auf jede Anfrage des erzeugten Clients die Kopfzeile <c>apiToken</c> aus dem
/// <see cref="ITanssTokenProvider"/>.
/// </summary>
/// <remarks>
/// <para><b>Die Kopfzeile heißt <c>apiToken</c>, nicht <c>Authorization</c>.</b> So steht es in der
/// Spezifikation, Abschnitt <c>components.securitySchemes.ApiTokenAuth</c>: <c>type: apiKey</c>, <c>in:
/// header</c>, <c>name: apiToken</c>. Die Beschreibung von <c>POST /api/v1/login</c> sagt es noch einmal
/// ausdrücklich: „send it verbatim under the <c>apiToken</c> header (note: this is the literal header
/// name, <b>not</b> the standard <c>Authorization</c> header)“. Dieselbe Kopfzeile tragen auch alle
/// Modul-Verfahren (<c>ErpToken</c>, <c>PhoneToken</c>, <c>RemoteSupportToken</c>, …).</para>
/// <para><b>Das Präfix <c>Bearer </c> gehört zum Wert</b> — und zwar genau einmal. Laut Spezifikation
/// „The <c>apiKey</c> value already includes the literal <c>Bearer </c> prefix“; ein Token aus <c>POST
/// /api/v1/login</c> bringt es also mit und wird unverändert gesendet. Fehlt es (etwa bei einem von Hand
/// kopierten JWT), wird es vorangestellt: Der Server schneidet das Präfix ab, bevor er das Token prüft —
/// ohne Präfix schlägt die Prüfung fehl, und die 403 ist von der eines abgelaufenen Tokens nicht zu
/// unterscheiden (Verhalten des Servers, gegen 10.10 geprüft).</para>
/// <para><b>Kein Token, keine Kopfzeile.</b> Liefert der Anbieter <c>null</c> oder Leerraum, geht die
/// Anfrage ohne <c>apiToken</c> hinaus. Das ist für die tokenfreien Routen richtig — <c>POST
/// /api/v1/login</c> trägt in der Spezifikation <c>security: []</c>, <c>GET /.well-known/jwks.json</c>
/// ist öffentlich — und für alle anderen antwortet TANSS mit 403, was <see cref="TanssApiException"/>
/// sauber ausweist.</para>
/// <para><b>Gelesen wird bei jeder Anfrage neu.</b> Ein Sitzungstoken läuft nach vier Stunden ab; ein
/// gemerkter Wert erzeugte nach jeder Erneuerung reihenweise 403.</para>
/// </remarks>
public sealed class TanssApiTokenAuthenticationProvider : IAuthenticationProvider
{
    /// <summary>Die Kopfzeile, die TANSS liest.</summary>
    public const string HeaderName = "apiToken";

    /// <summary>Das Präfix, das der Wert genau einmal trägt.</summary>
    public const string BearerPrefix = "Bearer ";

    private readonly ITanssTokenProvider _tokens;

    /// <summary>Baut den Anbieter.</summary>
    /// <param name="tokens">Woher das Token kommt. Wird bei jeder Anfrage befragt.</param>
    public TanssApiTokenAuthenticationProvider(ITanssTokenProvider tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _tokens = tokens;
    }

    /// <inheritdoc />
    public Task AuthenticateRequestAsync(RequestInformation request,
                                         Dictionary<string, object>? additionalAuthenticationContext = null,
                                         CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Remove(HeaderName);
        if (HeaderValue(_tokens.GetToken()) is { } value)
        {
            request.Headers.Add(HeaderName, value);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Der Wert der Kopfzeile: das Token mit genau einem <c>Bearer </c> davor;
    /// <c>null</c>, wenn gar kein Token vorliegt.
    /// </summary>
    /// <param name="token">Das gespeicherte Token, mit oder ohne Präfix, mit oder ohne Leerraum.</param>
    public static string? HeaderValue(string? token)
    {
        string raw = token?.Trim() ?? string.Empty;
        if (raw.Length == 0 || string.Equals(raw, BearerPrefix.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // "Bearer" ohne Token dahinter ist kein Token, sondern ein leerer Speicher.
            return null;
        }

        if (raw.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return raw;
        }

        // Ein von Hand eingetragenes "bearer …" zählt als vorhandenes Präfix, wird aber auf die wörtliche
        // Schreibweise gebracht: Der Server schneidet "Bearer " ab, nicht irgendeine Schreibweise davon.
        return raw.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? BearerPrefix + raw[BearerPrefix.Length..].TrimStart()
            : BearerPrefix + raw;
    }
}
