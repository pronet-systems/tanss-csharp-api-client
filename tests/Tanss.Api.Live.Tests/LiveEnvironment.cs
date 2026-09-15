using Xunit;

namespace Tanss.Api.Live.Tests;

/// <summary>
/// Die Zugangsdaten für die Live-Tests — und die Entscheidung, ob sie überhaupt laufen.
/// </summary>
/// <remarks>
/// Die Tests dieses Projekts sprechen mit einer <b>echten</b> TANSS-Instanz. Ohne die drei
/// Umgebungsvariablen <see cref="BaseUrlVariable"/>, <see cref="UserVariable"/> und
/// <see cref="PasswordVariable"/> gibt es keine Instanz, gegen die sie laufen könnten; sie
/// werden dann übersprungen. Auf einem Bauserver ohne diese Variablen ist der Lauf also grün,
/// ohne dass ein einziges Paket das Netz verlässt.
/// </remarks>
internal static class LiveEnvironment
{
    /// <summary>Die vollständige Basis der Schnittstelle, etwa <c>https://tanss.example.de/backend</c>.</summary>
    public const string BaseUrlVariable = "TANSS_BASE_URL";

    /// <summary>Der Anmeldename.</summary>
    public const string UserVariable = "TANSS_USER";

    /// <summary>Das Kennwort.</summary>
    public const string PasswordVariable = "TANSS_PASSWORD";

    /// <summary>
    /// Der Dashboard-Schlüssel eines Mitarbeiters (<c>TnsEmployee.dashboardApiKey</c>) für die Anmeldung
    /// über die Kopfzeile <c>dbapikey</c> (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README,
    /// Abschnitt „Gemessen, nicht geraten“).
    /// </summary>
    /// <remarks>
    /// <b>Zusätzlich</b> zu den drei anderen Variablen und <b>freiwillig</b>: Die Schnittstelle gibt
    /// diesen Schlüssel nicht heraus, er wird in der TANSS-Verwaltung gepflegt. Ohne ihn wird nur die
    /// Probe übersprungen, die einen gültigen Schlüssel braucht; dass der Weg überhaupt bedient wird,
    /// prüft die Probe mit dem erfundenen Schlüssel auch ohne ihn.
    /// </remarks>
    public const string DashboardKeyVariable = "TANSS_DBAPIKEY";

    /// <summary>Der Text, der im Testlauf an der übersprungenen Probe steht.</summary>
    public const string SkipReason =
        "Live-Test: setze TANSS_BASE_URL, TANSS_USER und TANSS_PASSWORD, um ihn laufen zu lassen.";

    /// <summary>Der Grund an einer Probe, die zusätzlich einen Dashboard-Schlüssel braucht.</summary>
    public const string DashboardSkipReason =
        "Live-Test: setze zusätzlich TANSS_DBAPIKEY (TnsEmployee.dashboardApiKey aus der "
        + "TANSS-Verwaltung), um ihn laufen zu lassen.";

    /// <summary>Die Basisadresse aus der Umgebung; <c>null</c>, wenn keine gesetzt ist.</summary>
    public static string? BaseUrl => Value(BaseUrlVariable);

    /// <summary>Der Anmeldename aus der Umgebung.</summary>
    public static string? User => Value(UserVariable);

    /// <summary>Das Kennwort aus der Umgebung.</summary>
    public static string? Password => Value(PasswordVariable);

    /// <summary>Der Dashboard-Schlüssel aus der Umgebung; <c>null</c>, wenn keiner gesetzt ist.</summary>
    public static string? DashboardKey => Value(DashboardKeyVariable);

    /// <summary>Ist alles da, was die Anmeldung über <c>dbapikey</c> braucht?</summary>
    public static bool HasDashboardKey => IsConfigured && DashboardKey is { Length: > 0 };

    /// <summary>Sind alle drei Variablen gesetzt und ist die Adresse brauchbar?</summary>
    public static bool IsConfigured =>
        BaseUrl is { Length: > 0 } url
        && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
        && User is { Length: > 0 }
        && Password is { Length: > 0 };

    /// <summary>Die Einstellungen für den Zugang.</summary>
    /// <remarks>
    /// <b>Ohne <c>EmployeeId</c>:</b> Ein Anmeldetoken (JWT-Typ <c>ACCESS</c>) führt den Mitarbeiter im
    /// Claim <c>sub</c>; der Abfrageparameter <c>loggedInUserId</c> ist dann weder nötig noch wirksam
    /// (gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt „Gemessen, nicht geraten“). Die
    /// Live-Tests lassen ihn deshalb weg und prüfen damit zugleich, dass es ohne ihn geht.
    /// </remarks>
    public static TanssApiOptions Options() => new()
    {
        BaseUrl = new Uri(BaseUrl!, UriKind.Absolute),
        EmployeeId = null,
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>Eine Umgebungsvariable ohne Randleerraum; <c>null</c>, wenn sie leer ist.</summary>
    private static string? Value(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

/// <summary>
/// Ein <see cref="FactAttribute"/>, das sich selbst überspringt, solange die Umgebung keine
/// Instanz nennt — die hausgemachte Fassung von „SkippableFact“.
/// </summary>
/// <remarks>
/// Der Wert von <see cref="FactAttribute.Skip"/> wird beim Auffinden der Tests gesetzt, also
/// einmal je Lauf. Ergebnis im Bericht: „übersprungen“ mit
/// <see cref="LiveEnvironment.SkipReason"/> als Grund — nicht „bestanden“, damit niemand einen
/// Lauf ohne Instanz für einen erfolgreichen Live-Lauf hält.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class LiveFactAttribute : FactAttribute
{
    /// <summary>Baut das Merkmal und setzt den Übersprunggrund, falls die Umgebung nicht taugt.</summary>
    public LiveFactAttribute()
    {
        if (!LiveEnvironment.IsConfigured)
        {
            Skip = LiveEnvironment.SkipReason;
        }
    }
}

/// <summary>
/// Wie <see cref="LiveFactAttribute"/>, verlangt aber zusätzlich
/// <see cref="LiveEnvironment.DashboardKeyVariable"/>.
/// </summary>
/// <remarks>
/// Für die eine Probe, die einen <b>gültigen</b> Dashboard-Schlüssel braucht. Der ist nirgends
/// abzufragen — die Schnittstelle gibt <c>TnsEmployee.dashboardApiKey</c> nicht heraus —, also
/// bleibt die Probe ohne ihn übersprungen. Dass der Anmeldeweg bedient wird, prüft die Probe
/// mit dem erfundenen Schlüssel; die braucht ihn nicht.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class DashboardLiveFactAttribute : FactAttribute
{
    /// <summary>Baut das Merkmal und setzt den Übersprunggrund, falls kein Schlüssel gesetzt ist.</summary>
    public DashboardLiveFactAttribute()
    {
        if (!LiveEnvironment.IsConfigured)
        {
            Skip = LiveEnvironment.SkipReason;
        }
        else if (!LiveEnvironment.HasDashboardKey)
        {
            Skip = LiveEnvironment.DashboardSkipReason;
        }
    }
}
