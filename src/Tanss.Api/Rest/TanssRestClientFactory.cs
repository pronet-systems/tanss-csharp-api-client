using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Microsoft.Kiota.Serialization.Form;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.Kiota.Serialization.Multipart;
using Microsoft.Kiota.Serialization.Text;

namespace Tanss.Api.Rest;

/// <summary>
/// Baut den erzeugten <see cref="TanssRestClient"/> so, dass er die Regeln der Schnittstelle
/// einhält.
/// </summary>
/// <remarks>
/// <para>Der erzeugte Client weiß nichts von TANSS: nicht, dass die Kopfzeile <c>apiToken</c>
/// heißt, nicht, dass eine <c>USER</c>-Route den Parameter <c>loggedInUserId</c> braucht, nicht,
/// dass jeder Aufruf unter <c>/api/v1/jwts</c> ein neues Token prägt. Diese Fabrik trägt das
/// nach — aus <see cref="TanssApiOptions"/> (Adresse, Mitarbeiter, Zeitgrenze, Proxy, TLS) und
/// einem <see cref="ITanssTokenProvider"/>.</para>
/// <para><b>Die Basisadresse wird unverändert vorangestellt.</b> Die Vorlagen des erzeugten
/// Clients beginnen mit <c>{+baseurl}</c>, und Kiota setzt dort den <c>BaseUrl</c> des Adapters
/// ein — vor dem Bau des Clients, sonst stünde die Vorgabe <c>https://{host}</c> aus der
/// Spezifikation darin (Abschnitt <c>servers</c>). Aus
/// <c>https://tanss.example.de/backend</c> und <c>{+baseurl}/api/v1/tickets/own</c> wird so
/// <c>https://tanss.example.de/backend/api/v1/tickets/own</c>.</para>
/// <para><b>Wiederholt wird nur Lesendes.</b> Kiota bringt einen Wiederholungs-Handler mit, der
/// bei 429, 503 und 504 jede Anfrage erneut schickt — auch <c>POST</c>. Die Wiederholung ist
/// hier auf <c>GET</c> beschränkt und dort noch einmal auf Pfade außerhalb von
/// <c>/api/v1/jwts</c>: Laut Beschreibung dieser Operation prägt sie bei jedem Aufruf ein neues
/// Token und schreibt es in das Token-Protokoll („the <c>info</c> parameter is stored in the
/// token log so the issued token can be tracked / revoked“) — ein zweiter Versuch hinterließe
/// ein zweites Token.</para>
/// <para><b>Wer freigeben will, hält den Adapter.</b>
/// <see cref="Create(TanssApiOptions, ITanssTokenProvider, ILogger?, HttpMessageHandler?)"/>
/// gibt den Client zurück, und der kennt seinen Adapter nur intern. Für einen Wirt, der die
/// Verbindungen selbst verwaltet, gibt es
/// <see cref="CreateRequestAdapter(TanssApiOptions, ITanssTokenProvider, ILogger?, HttpMessageHandler?)"/>:
/// Der Adapter ist <see cref="IDisposable"/>, und <c>new TanssRestClient(adapter)</c> ist dann
/// der eine fehlende Schritt.</para>
/// </remarks>
public static class TanssRestClientFactory
{
    /// <summary>Der Pfad, unter dem jeder Aufruf ein Token prägt und deshalb nie wiederholt wird.</summary>
    public const string MintPrefix = "/api/v1/jwts";

    /// <summary>Baut den Client samt eigener Verbindungsschicht.</summary>
    /// <param name="options">Adresse, Mitarbeiter-Id, Zeitgrenze, Proxy, TLS-Prüfung.</param>
    /// <param name="tokens">Woher das Token kommt. Wird bei jeder Anfrage befragt.</param>
    /// <param name="logger">Wahlweise; Aufbau auf <c>Debug</c>, abgeschaltete TLS-Prüfung auf <c>Warning</c>.</param>
    /// <param name="handler">
    /// Wahlweise das letzte Glied der Kette — für Tests mit einer Attrappe oder für einen Wirt,
    /// der seine Verbindungen selbst verwaltet. Ohne Angabe baut
    /// <see cref="CreateFinalHandler"/> eines aus <paramref name="options"/>; Proxy und
    /// TLS-Prüfung wirken dann nur in diesem Fall.
    ///</param>
    public static TanssRestClient Create(TanssApiOptions options, ITanssTokenProvider tokens,
                                         ILogger? logger = null, HttpMessageHandler? handler = null) =>
        new(CreateRequestAdapter(options, tokens, logger, handler));

    /// <summary>Baut den Adapter samt eigener Verbindungsschicht — für Wirte, die ihn selbst halten und freigeben.</summary>
    /// <param name="options">Adresse, Mitarbeiter-Id, Zeitgrenze, Proxy, TLS-Prüfung.</param>
    /// <param name="tokens">Woher das Token kommt.</param>
    /// <param name="logger">Wahlweise.</param>
    /// <param name="handler">Wahlweise das letzte Glied der Kette.</param>
    public static HttpClientRequestAdapter CreateRequestAdapter(TanssApiOptions options, ITanssTokenProvider tokens,
                                                                ILogger? logger = null,
                                                                HttpMessageHandler? handler = null) =>
        CreateRequestAdapter(options, tokens, CreateHttpClient(options, logger, handler), logger);

    /// <summary>Baut den Adapter auf einem vorhandenen <see cref="HttpClient"/> auf.</summary>
    /// <remarks>
    /// Gedacht für den Fall, dass Sitzung und erzeugter Client sich <b>eine</b>
    /// Verbindungsschicht teilen sollen — so hält es <see cref="TanssApi"/>.
    /// </remarks>
    /// <param name="options">Adresse und Mitarbeiter-Id.</param>
    /// <param name="tokens">Woher das Token kommt.</param>
    /// <param name="http">Der Client, den <see cref="CreateHttpClient"/> gebaut hat.</param>
    /// <param name="logger">Wahlweise.</param>
    public static HttpClientRequestAdapter CreateRequestAdapter(TanssApiOptions options, ITanssTokenProvider tokens,
                                                                HttpClient http, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(http);

        // Dieselben Werkzeuge, die auch der Konstruktor des erzeugten Clients anmeldet. Doppelt
        // angemeldet ist nichts: Die Register nehmen jeden Typ nur einmal. So ist auch ein
        // Adapter ohne TanssRestClient benutzbar.
        RegisterSerializers();

        HttpClientRequestAdapter adapter = new(new TanssApiTokenAuthenticationProvider(tokens), httpClient: http)
        {
            BaseUrl = BaseUrlOf(options),
        };

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("Kiota-Client für {BaseUrl} gebaut: Mitarbeiter {EmployeeId}, Zeitgrenze {Timeout:0} s.",
                            adapter.BaseUrl, options.EmployeeId, options.Timeout.TotalSeconds);
        }

        return adapter;
    }

    /// <summary>
    /// Baut die Verbindungsschicht: Kiotas Kette, die <c>loggedInUserId</c>-Regel, Zeitgrenze,
    /// Proxy und TLS-Prüfung.
    /// </summary>
    /// <remarks>
    /// Die Basisadresse steht danach auch als <see cref="HttpClient.BaseAddress"/> — mit
    /// Schrägstrich am Ende, damit ein angehängter Pfad sie nicht ersetzt. Kiota braucht das
    /// nicht (der Adapter baut absolute Adressen), <see cref="TanssSession"/> schon.
    /// </remarks>
    /// <param name="options">Adresse, Mitarbeiter-Id, Zeitgrenze, Proxy, TLS-Prüfung.</param>
    /// <param name="logger">Wahlweise.</param>
    /// <param name="handler">Wahlweise das letzte Glied der Kette.</param>
    public static HttpClient CreateHttpClient(TanssApiOptions options, ILogger? logger = null,
                                              HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        string baseUrl = BaseUrlOf(options);
        string basePath = ApiPaths.BasePathOf(baseUrl);

        // Kiotas eigene Kette (Wiederholung, Umleitung, Parameter-Namen, ...) und dahinter,
        // unmittelbar vor der Leitung, die loggedInUserId-Regel.
        IList<DelegatingHandler> handlers = KiotaClientFactory.CreateDefaultHandlers([ReadOnlyRetry(basePath)]);
        handlers.Add(new LoggedInUserIdHandler(options.EmployeeId, basePath, logger));

        HttpClient http = KiotaClientFactory.Create(handlers, handler ?? CreateFinalHandler(options, logger));
        http.Timeout = options.Timeout;
        http.BaseAddress = new Uri(baseUrl + "/", UriKind.Absolute);
        return http;
    }

    /// <summary>
    /// Das letzte Glied der Kette: Proxy und TLS-Prüfung aus den Einstellungen, Umleitungen und
    /// Entpacken so, wie Kiota sie erwartet.
    /// </summary>
    /// <param name="options">Proxy und TLS-Prüfung.</param>
    /// <param name="logger">Wahlweise; eine abgeschaltete TLS-Prüfung wird als <c>Warning</c> gemeldet.</param>
    public static SocketsHttpHandler CreateFinalHandler(TanssApiOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        SocketsHttpHandler handler = new()
        {
            // Umleitungen erledigt Kiotas RedirectHandler; die Leitung darf ihm nicht vorgreifen.
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,

            // Zugangsdaten reisen bei der Anmeldung in Kopfzeilen (gemessen gegen TANSS 10.10 am
            // 2026-09-15), und der Server liest sie als Latin-1-Bytes, die er anschliessend als UTF-8
            // deutet (vom Server so umgesetzt, gegen 10.10 geprueft am 2026-09-15). TanssSession legt die
            // UTF-8-Bytes deshalb als Latin-1-Zeichenkette in die Kopfzeile. Die Vorgabe von .NET
            // schreibt Kopfzeilen als ASCII und wiese jedes Zeichen darueber mit "Request headers must
            // contain only ASCII characters" ab; erst diese Wahl bringt die Bytes unveraendert auf die
            // Leitung. Ein Kennwort mit Umlauten scheitert sonst.
            RequestHeaderEncodingSelector = static (_, _) => Encoding.Latin1,
        };

        if (options.Proxy is { } address)
        {
            WebProxy proxy = new(address);
            if (!string.IsNullOrEmpty(options.ProxyUser))
            {
                proxy.Credentials = new NetworkCredential(options.ProxyUser, options.ProxyPassword ?? string.Empty);
            }

            handler.Proxy = proxy;
            handler.UseProxy = true;
        }

        if (!options.ValidateTls)
        {
            // NOTBEHELF: Schaltet die Zertifikatspruefung vollstaendig ab und macht die
            // Verbindung gegen einen Angreifer in der Mitte wertlos.
            logger?.LogWarning("TLS-Prüfung für {BaseUrl} abgeschaltet: Die Verbindung ist gegen einen Angreifer in der Mitte wertlos.",
                               options.BaseUrl);
#pragma warning disable CA5359 // Genau das ist der Zweck des Schalters; die Warnung steht eine Zeile darueber.
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
#pragma warning restore CA5359
        }

        return handler;
    }

    /// <summary>
    /// Die geprüfte Basisadresse als Zeichenkette, ohne Schrägstrich am Ende — so, wie Kiota sie
    /// als <c>BaseUrl</c> des Adapters erwartet.
    /// </summary>
    /// <param name="options">Die Einstellungen.</param>
    /// <exception cref="ArgumentException">Die Adresse ist keine absolute http(s)-Adresse.</exception>
    public static string BaseUrlOf(TanssApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Uri baseUrl = options.BaseUrl;
        if (baseUrl is null || !baseUrl.IsAbsoluteUri
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                $"Die Basisadresse \"{options.BaseUrl}\" ist keine absolute http(s)-Adresse. Erwartet "
                + "wird die vollständige Basis der Schnittstelle einschließlich des Pfads, unter dem "
                + "die Installation sie ausliefert (auf einer TANSS-Installation /backend), etwa "
                + "https://tanss.example.de/backend.",
                nameof(options));
        }

        return baseUrl.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>
    /// Darf diese Antwort zu einer Wiederholung führen? Nur bei 429, 503 oder 504 — Kiotas
    /// eigene Statusregel — und auch dann nur auf ein <c>GET</c> außerhalb von
    /// <see cref="MintPrefix"/>.
    /// </summary>
    /// <remarks>
    /// Die Statusprüfung steht hier und nicht nur bei Kiota: Ein eigener <c>ShouldRetry</c>
    /// <b>ersetzt</b> Kiotas Vorgabe, er ergänzt sie nicht. Ohne die Prüfung liefe jede 200 in
    /// die Wiederholungsschleife — bis zur Zeitgrenze.
    /// </remarks>
    /// <param name="response">Die Antwort samt der Anfrage, die zu ihr geführt hat.</param>
    /// <param name="basePath">Der Pfadanteil der Basisadresse.</param>
    internal static bool IsRepeatableRead(HttpResponseMessage? response, string basePath)
    {
        // Ohne Anfrage keine Aussage - und im Zweifel keine Wiederholung.
        if (response is not { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                                          or HttpStatusCode.GatewayTimeout }
            || response.RequestMessage is not { } request
            || request.Method != HttpMethod.Get
            || request.RequestUri is not { IsAbsoluteUri: true } uri)
        {
            return false;
        }

        string path = ApiPaths.BelowBase(uri.AbsolutePath, basePath);
        return !path.Equals(MintPrefix, StringComparison.Ordinal)
               && !path.StartsWith(MintPrefix + "/", StringComparison.Ordinal);
    }

    private static RetryHandlerOption ReadOnlyRetry(string basePath) => new()
    {
        ShouldRetry = (_, _, response) => IsRepeatableRead(response, basePath),
    };

    private static void RegisterSerializers()
    {
        ApiClientBuilder.RegisterDefaultSerializer<JsonSerializationWriterFactory>();
        ApiClientBuilder.RegisterDefaultSerializer<TextSerializationWriterFactory>();
        ApiClientBuilder.RegisterDefaultSerializer<FormSerializationWriterFactory>();
        ApiClientBuilder.RegisterDefaultSerializer<MultipartSerializationWriterFactory>();
        ApiClientBuilder.RegisterDefaultDeserializer<JsonParseNodeFactory>();
        ApiClientBuilder.RegisterDefaultDeserializer<TextParseNodeFactory>();
        ApiClientBuilder.RegisterDefaultDeserializer<FormParseNodeFactory>();
    }
}
