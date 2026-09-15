namespace Tanss.Api;

/// <summary>
/// Woher der Wert der Kopfzeile <c>apiToken</c> kommt.
/// </summary>
/// <remarks>
/// <para>Die Spezifikation beschreibt im Abschnitt <c>securitySchemes.ApiTokenAuth</c> ein
/// <c>apiKey</c>-Verfahren mit <c>in: header</c> und <c>name: apiToken</c>: „JWT obtained from
/// <c>POST /api/v1/login</c>. The value already includes the literal <c>Bearer </c> prefix —
/// send it verbatim in the <c>apiToken</c> header.“ Mehr als diese eine Zeichenkette braucht
/// die Bibliothek nicht zu wissen.</para>
/// <para>Gefragt wird vor <b>jeder</b> Anfrage. Ein Token läuft nach vier Stunden ab
/// (Spezifikation, Beschreibung von <c>POST /api/v1/login</c>), und ein einmal gemerkter Wert
/// erzeugte nach jeder Erneuerung reihenweise 403. Eine Umsetzung muss deshalb billig und
/// nebenläufig aufrufbar sein.</para>
/// <para><c>null</c> oder leer heißt: kein Token. Der Anbieter setzt dann keine Kopfzeile —
/// richtig für die Anmeldung selbst, die in der Spezifikation mit <c>security: []</c> als
/// tokenfrei ausgewiesen ist.</para>
/// </remarks>
public interface ITanssTokenProvider
{
    /// <summary>Das aktuelle Token, mit oder ohne <c>Bearer </c>; <c>null</c>, wenn keines vorliegt.</summary>
    string? GetToken();
}

/// <summary>Ein unveränderlicher Wert — für ein in der Konfiguration hinterlegtes Modul-Token.</summary>
/// <remarks>
/// Modul-Token (ERP, PHONE, REMOTE_SUPPORT, TIMESTAMP, …) werden in der TANSS-Administration
/// erzeugt und laufen typischerweise erst nach einem Jahr ab
/// (<c>GET /api/v1/jwts/{ext_program}</c>, Vorgabe von <c>duration</c>: 31536000000 ms). Für sie
/// genügt ein fester Wert; ein Sitzungstoken gehört in den <see cref="MutableTokenProvider"/>.
/// </remarks>
public sealed class StaticTokenProvider : ITanssTokenProvider
{
    private readonly string? _token;

    /// <summary>Baut den Anbieter.</summary>
    /// <param name="token">Das Token, mit oder ohne <c>Bearer </c>; <c>null</c> für „keines“.</param>
    public StaticTokenProvider(string? token) => _token = token;

    /// <inheritdoc />
    public string? GetToken() => _token;
}

/// <summary>
/// Ein Wert, der sich ändern darf — der Platz für das Sitzungstoken aus
/// <c>POST /api/v1/login</c>.
/// </summary>
/// <remarks>
/// <para>Lesen und Schreiben sind nebenläufig sicher (<see cref="Volatile"/> auf eine
/// Objektreferenz; Zuweisungen von Referenzen sind atomar). Ein Leser sieht damit entweder das
/// alte oder das neue Token, nie einen halben Wert.</para>
/// <para><see cref="TanssSession"/> schreibt hier nach Anmeldung und Erneuerung den Wert aus
/// <c>content.apiKey</c> hinein, sodass alle laufenden Clients ohne Neubau weiterarbeiten.</para>
/// </remarks>
public sealed class MutableTokenProvider : ITanssTokenProvider
{
    private string? _token;

    /// <summary>Baut den Anbieter, wahlweise mit einem Anfangswert.</summary>
    /// <param name="token">Das erste Token; <c>null</c>, solange noch keine Anmeldung erfolgt ist.</param>
    public MutableTokenProvider(string? token = null) => _token = token;

    /// <summary>Das aktuelle Token. Schreiben wirkt sofort auf alle Clients, die diesen Anbieter halten.</summary>
    public string? Token
    {
        get => Volatile.Read(ref _token);
        set => Volatile.Write(ref _token, value);
    }

    /// <inheritdoc />
    public string? GetToken() => Token;
}
