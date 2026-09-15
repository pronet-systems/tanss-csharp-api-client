using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Tanss.Api.Rest;

namespace Tanss.Api;

/// <summary>
/// Der Einstieg: eine Verbindungsschicht, darauf der vollständige erzeugte Client und die drei
/// Token-Abläufe.
/// </summary>
/// <remarks>
/// <para><see cref="Rest"/> ist die ganze Schnittstelle — jede Operation der kombinierten
/// Spezifikation, typisiert. Es gibt daneben keine Kurzwege, keine Repositories und keine
/// Bequemlichkeiten: Was TANSS anbietet, steht in der Spezifikation, und was darüber hinaus
/// nützlich ist, entscheidet die aufrufende Anwendung, nicht diese Bibliothek.</para>
/// <para><see cref="Session"/> deckt ab, was der erzeugte Client nicht kann, weil die
/// Spezifikation dafür kein Schema führt: Anmelden, Erneuern, Prägen.</para>
/// <para>Beide senden über <b>eine</b> Verbindungsschicht mit denselben Regeln — Kopfzeile
/// <c>apiToken</c>, <c>loggedInUserId</c> auf <c>USER</c>-Routen, Wiederholung nur für lesende
/// Aufrufe außerhalb von <c>/api/v1/jwts</c>.</para>
/// </remarks>
public sealed class TanssApi : IDisposable
{
    private readonly HttpClientRequestAdapter _adapter;
    private readonly HttpClient _http;

    private TanssApi(TanssApiOptions options, ITanssTokenProvider tokens, HttpClient http,
                     HttpClientRequestAdapter adapter, TanssRestClient rest, TanssSession session)
    {
        Options = options;
        Tokens = tokens;
        _http = http;
        _adapter = adapter;
        Rest = rest;
        Session = session;
    }

    /// <summary>Die Einstellungen, mit denen dieser Zugang gebaut wurde.</summary>
    public TanssApiOptions Options { get; }

    /// <summary>Der Token-Anbieter, den jede Anfrage befragt.</summary>
    public ITanssTokenProvider Tokens { get; }

    /// <summary>Der erzeugte Client: jede Operation der Spezifikation, typisiert.</summary>
    public TanssRestClient Rest { get; }

    /// <summary>Anmelden, Erneuern, Prägen.</summary>
    public TanssSession Session { get; }

    /// <summary>Baut den Zugang.</summary>
    /// <param name="options">Adresse, Mitarbeiter-Id, Zeitgrenze, Proxy, TLS-Prüfung.</param>
    /// <param name="tokens">
    /// Woher das Token kommt. Ein <see cref="MutableTokenProvider"/> nimmt das Ergebnis von
    /// <see cref="TanssSession.LoginAsync"/> selbsttätig auf.
    ///</param>
    /// <param name="logger">Wahlweise.</param>
    /// <param name="handler">
    /// Wahlweise das letzte Glied der Kette — für Tests mit einer Attrappe oder für einen Wirt,
    /// der seine Verbindungen selbst verwaltet. Er wird mit diesem Objekt freigegeben.
    ///</param>
    /// <exception cref="ArgumentException">Die Basisadresse ist keine absolute http(s)-Adresse.</exception>
    public static TanssApi Create(TanssApiOptions options, ITanssTokenProvider tokens, ILogger? logger = null,
                                  HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokens);

        HttpClient http = TanssRestClientFactory.CreateHttpClient(options, logger, handler);
        try
        {
            HttpClientRequestAdapter adapter = TanssRestClientFactory.CreateRequestAdapter(options, tokens, http, logger);
            return new TanssApi(options, tokens, http, adapter, new TanssRestClient(adapter),
                                new TanssSession(http, tokens, logger));
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    /// <summary>Gibt die Verbindungsschicht frei — einschließlich eines übergebenen Handlers.</summary>
    public void Dispose()
    {
        _adapter.Dispose();
        _http.Dispose();
    }
}
