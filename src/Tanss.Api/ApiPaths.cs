namespace Tanss.Api;

/// <summary>
/// Schneidet aus einer vollständigen Adresse den Teil heraus, auf den die Routenregeln rechnen.
/// </summary>
/// <remarks>
/// <see cref="TokenRoles"/> und der Endpunkt-Index kennen Pfade, wie die Spezifikation sie
/// führt: <c>/api/v1/tickets/own</c>. Auf der Leitung steht davor aber der Pfadanteil der
/// Basisadresse — auf einer TANSS-Installation im Regelfall <c>/backend</c>. Wer die Regel auf
/// den vollen Pfad anwendet, findet <c>/api/v1</c> nie am Anfang und hängt <c>loggedInUserId</c>
/// nirgends an; die Folge wäre eine 403 auf jeder Route.
/// </remarks>
internal static class ApiPaths
{
    private const string ApiMarker = "/api/";

    /// <summary>
    /// Der Pfad unterhalb der Basisadresse.
    /// </summary>
    /// <param name="absolutePath">Der Pfad der Anfrage, etwa <c>/backend/api/v1/timers</c>.</param>
    /// <param name="basePath">Der Pfadanteil der Basisadresse, etwa <c>/backend</c>; leer, wenn keiner.</param>
    /// <returns>
    /// Der Rest hinter der Basis. Passt die Basis nicht, gilt der erste Abschnitt <c>/api/</c>
    /// als Anfang — TANSS stellt alle Routen darunter, und eine abweichend gebaute Adresse soll
    /// die Regel nicht stumm aushebeln.
    ///</returns>
    public static string BelowBase(string absolutePath, string basePath)
    {
        if (basePath.Length > 0
            && absolutePath.StartsWith(basePath, StringComparison.Ordinal)
            && (absolutePath.Length == basePath.Length || absolutePath[basePath.Length] == '/'))
        {
            return absolutePath[basePath.Length..];
        }

        int index = absolutePath.IndexOf(ApiMarker, StringComparison.Ordinal);
        return index >= 0 ? absolutePath[index..] : absolutePath;
    }

    /// <summary>Der Pfadanteil der Basisadresse ohne Schrägstrich am Ende; leer, wenn die Adresse keiner ist.</summary>
    /// <param name="baseUrl">Die Basisadresse, etwa <c>https://tanss.example.de/backend</c>.</param>
    public static string BasePathOf(Uri baseUrl) =>
        baseUrl is { IsAbsoluteUri: true } ? baseUrl.AbsolutePath.TrimEnd('/') : string.Empty;

    /// <summary>Der Pfadanteil der Basisadresse ohne Schrägstrich am Ende; leer, wenn die Adresse keiner ist.</summary>
    /// <param name="baseUrl">Die Basisadresse als Zeichenkette.</param>
    public static string BasePathOf(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) ? uri.AbsolutePath.TrimEnd('/') : string.Empty;

    /// <summary>Entfernt Abfragezeichenkette und Fragment.</summary>
    /// <param name="path">Der Pfad, gegebenenfalls mit <c>?</c> oder <c>#</c>.</param>
    public static string StripQuery(string path)
    {
        int cut = path.IndexOfAny(['?', '#']);
        return cut < 0 ? path : path[..cut];
    }
}
