using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Tanss.Api.Rest;

/// <summary>
/// Hängt <c>loggedInUserId</c> an, wo TANSS daran den Mitarbeiter erkennt — und nur dort.
/// </summary>
/// <remarks>
/// <para><b>Die Regel ist Verhalten des Servers, keine Vermutung.</b> Vom Server so umgesetzt: Ein Token
/// vom Typ <c>TANSS_APP</c> bekommt die Rolle <c>ROLE_USER</c> nur dann zugesprochen, wenn die Anfrage
/// den Abfrageparameter <c>loggedInUserId</c> trägt — er benennt den Mitarbeiter, in dessen Kontext
/// gearbeitet wird. Ohne ihn fehlt dieser Kontext, und jede Route mit der Rolle <c>USER</c> antwortet 403
/// (Verhalten des Servers, gegen 10.10 geprüft). Ein Login-Token (<c>ACCESS</c>) führt den Mitarbeiter im
/// Claim <c>sub</c> mit; dort ist der Parameter überflüssig, aber unschädlich.</para>
/// <para><b>Gefragt wird die Rollentabelle, nicht der Pfadanfang.</b> Angehängt wird genau dort, wo
/// <see cref="TokenRoles.RequiredFor(string)"/> eine Rolle nennt, die <c>USER</c> einschließt
/// (<see cref="TokenRoles.IncludesUser(TokenRole)"/>) — also auf den <c>USER</c>-Modulen unter
/// <c>/api/v1</c> samt der gemischten Zeilen. Die Modul-Präfixe (<c>/api/erp/v1</c>,
/// <c>/api/tanss.x/v1</c>, <c>/api/remoteSupports/v1</c>, …) bekommen ihn nie: Ihre Token kennen keinen
/// Mitarbeiter, und die Spezifikation führt den Parameter dort bei keiner Operation.</para>
/// <para><b>Nichts wird überschrieben.</b> Steht <c>loggedInUserId</c> schon in der Abfrage — in welcher
/// Schreibweise auch immer —, bleibt der Wert des Aufrufers stehen. Der erzeugte Client setzt
/// Abfrageparameter aus der Spezifikation; wer ihn dort angibt, meint einen anderen Mitarbeiter.
/// Verglichen wird ohne Rücksicht auf Groß- und Kleinschreibung, sonst stünden zwei nebeneinander.</para>
/// <para><b>Ohne Mitarbeiter-Id geschieht nichts.</b> Ist <see cref="TanssApiOptions.EmployeeId"/>
/// <c>null</c>, hängt der Handler nirgends etwas an — richtig für Modul-Token.</para>
/// <para>Der Handler sitzt am Ende der Kiota-Kette, unmittelbar vor der Verbindungsschicht. Er sieht
/// damit die fertige Adresse und nicht die Vorlage mit Platzhaltern.</para>
/// </remarks>
public sealed class LoggedInUserIdHandler : DelegatingHandler
{
    /// <summary>Der Parametername, so wie TANSS ihn erwartet.</summary>
    public const string ParameterName = "loggedInUserId";

    private readonly string? _employeeId;
    private readonly string _basePath;
    private readonly ILogger? _logger;

    /// <summary>Baut den Handler aus den Einstellungen.</summary>
    /// <param name="options">Mitarbeiter-Id und Basisadresse; deren Pfadanteil wird vor der Prüfung abgeschnitten.</param>
    /// <param name="logger">Wahlweise; meldet auf <c>Trace</c>, wo der Parameter angehängt wurde.</param>
    public LoggedInUserIdHandler(TanssApiOptions options, ILogger? logger = null)
        : this(EmployeeIdOf(options), ApiPaths.BasePathOf(options.BaseUrl), logger)
    {
    }

    /// <summary>Baut den Handler aus den Einzelwerten.</summary>
    /// <param name="employeeId">Die Mitarbeiter-Id, die angehängt wird; <c>null</c> schaltet den Handler still.</param>
    /// <param name="basePath">Der Pfadanteil der Basisadresse, etwa <c>/backend</c>; leer, wenn keiner.</param>
    /// <param name="logger">Wahlweise; meldet auf <c>Trace</c>, wo der Parameter angehängt wurde.</param>
    public LoggedInUserIdHandler(int? employeeId, string basePath = "", ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        _employeeId = employeeId?.ToString(CultureInfo.InvariantCulture);
        _basePath = basePath.TrimEnd('/');
        _logger = logger;
    }

    /// <summary>Steht der Parameter — in irgendeiner Schreibweise — schon in der Abfrage?</summary>
    /// <param name="query">Die Abfragezeichenkette, mit oder ohne führendes <c>?</c>.</param>
    /// <param name="name">Der gesuchte Name.</param>
    public static bool HasParameter(string query, string name)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(name);

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int split = pair.IndexOf('=', StringComparison.Ordinal);
            string key = Uri.UnescapeDataString(split < 0 ? pair : pair[..split]);
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Hängt einen Abfrageparameter an, ohne die vorhandenen anzutasten.</summary>
    /// <param name="uri">Die vollständige Adresse.</param>
    /// <param name="name">Der Name des Parameters.</param>
    /// <param name="value">Sein Wert.</param>
    public static Uri Append(Uri uri, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        string existing = uri.Query.TrimStart('?').TrimEnd('&');
        string address = uri.GetLeftPart(UriPartial.Path)
            + "?" + (existing.Length == 0 ? string.Empty : existing + "&")
            + Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
        return new Uri(address, UriKind.Absolute);
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                           CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_employeeId is { } employeeId && request.RequestUri is { IsAbsoluteUri: true } uri)
        {
            string path = ApiPaths.BelowBase(uri.AbsolutePath, _basePath);
            if (TokenRoles.IncludesUser(TokenRoles.RequiredFor(path)) && !HasParameter(uri.Query, ParameterName))
            {
                request.RequestUri = Append(uri, ParameterName, employeeId);
                if (_logger?.IsEnabled(LogLevel.Trace) == true)
                {
                    _logger.LogTrace("{Method} {Path}: {Parameter}={Value} angehängt.",
                                     request.Method.Method, path, ParameterName, employeeId);
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static int? EmployeeIdOf(TanssApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.EmployeeId;
    }
}
