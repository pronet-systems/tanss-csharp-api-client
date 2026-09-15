using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tanss.Api;

/// <summary>
/// Was der Endpunkt-Index über eine Operation weiß: dokumentiert, auf 10.10 vorhanden, mit dem Token
/// erreichbar, Token-Klasse und Rollen.
/// </summary>
/// <remarks>
/// <para>Der generierte Client kennt alle 1137 Operationen: 842 aus der offiziellen
/// Schnittstellenbeschreibung und 295, die dort fehlen, die der Server aber bedient. Die
/// geprüfte Instanz läuft auf 10.10 und bedient 958 davon; für 11 davon passt aber keine Regel der
/// Sicherheitskonfiguration (Token-Klasse <c>denied</c>), sodass 947 wirklich erreichbar sind. Alles
/// andere antwortet mit 403, 404 oder 405, ohne dass dem Aufrufer der Grund gesagt würde. Diese Klasse
/// liest den Endpunkt-Index, der zur Bauzeit eingebettet wird, und beantwortet die Frage vor dem Aufruf
/// statt danach.</para>
/// <para><b>Platzhalter zählen, ihre Namen nicht.</b> Der Index schreibt
/// <c>/api/v1/tickets/{ticketId}</c>, ein Aufrufer vielleicht <c>/api/v1/tickets/{id}</c> oder gleich
/// <c>/api/v1/tickets/4711</c>. Verglichen wird nach Ersetzen jedes <c>{…}</c> durch <c>{}</c>; findet
/// sich so nichts, gilt ein konkreter Wert an einer Platzhalterstelle als Treffer. Ein wörtlicher Pfad
/// hat Vorrang: <c>/api/v1/tickets/own</c> trifft die eigene Route und nicht <c>{ticketId}</c>.</para>
/// <para>Die drei Fragen bauen aufeinander auf: <see cref="IsKnown"/> — steht die Operation überhaupt im
/// Index; <see cref="IsDocumented"/> — steht sie in der offiziellen Schnittstellenbeschreibung (falsch
/// für die 295 nur auf dem Server gefundenen Routen, obwohl sie laufen); <see cref="IsAvailableOn1010"/>
/// — hat 10.10 sie; <see cref="IsReachableOn1010"/> — hat sie sie <i>und</i> lässt die
/// Sicherheitskonfiguration ein Token dorthin.</para>
/// </remarks>
public static partial class ApiAvailability
{
    /// <summary>Der Name der eingebetteten Ressource — festgelegt im Projekt über <c>LogicalName</c>.</summary>
    public const string ResourceName = "Tanss.Api.endpoint-index.json";

    /// <summary>
    /// Die Token-Klasse, für die in der Sicherheitskonfiguration von 10.10 keine Regel passt: nicht
    /// erreichbar.
    /// </summary>
    public const string DeniedTokenClass = "denied";

    private static readonly Lazy<Catalogue> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Die Server-Fassung, gegen die Verfügbarkeit und Rollen im Index geprüft sind
    /// (<c>10.10.0</c>).
    /// </summary>
    public static string ServerVersion => Loaded.Value.ServerVersion;

    /// <summary>Die Zahl der Einträge im Index — jede Operation des generierten Clients (1137).</summary>
    public static int Count => Loaded.Value.Count;

    /// <summary>
    /// Alle Operationen als <c>METHODE /pfad</c>, Platzhalter als <c>{}</c>. Ein Alias, der sich
    /// nur durch den Schrägstrich am Ende unterscheidet, fällt mit seinem Geschwister zusammen.
    /// </summary>
    public static IReadOnlyCollection<string> Operations => Loaded.Value.Operations.Keys;

    /// <summary>Steht die Operation im Index — dokumentiert oder nur auf dem Server gefunden?</summary>
    /// <param name="method">Das Verb, etwa <c>GET</c>; Groß- und Kleinschreibung ist gleich.</param>
    /// <param name="path">Der Pfad, mit oder ohne Abfragezeichenkette, Platzhalter beliebig
    /// benannt.</param>
    public static bool IsKnown(string method, string path) => Find(method, path) is not null;

    /// <summary>
    /// Steht die Operation in der offiziellen Schnittstellenbeschreibung? Falsch für die nur auf dem
    /// Server gefundenen.
    /// </summary>
    /// <param name="method">Das Verb.</param>
    /// <param name="path">Der Pfad.</param>
    public static bool IsDocumented(string method, string path) => Find(method, path)?.Documented ?? false;

    /// <summary>Hat TANSS 10.10 die Operation? Sagt nichts darüber, ob ein Token dorthin darf.</summary>
    /// <param name="method">Das Verb.</param>
    /// <param name="path">Der Pfad.</param>
    public static bool IsAvailableOn1010(string method, string path) => Find(method, path)?.In1010 ?? false;

    /// <summary>
    /// Bedient TANSS 10.10 die Operation wirklich: vorhanden <i>und</i> mit einer Token-Klasse ungleich
    /// <see cref="DeniedTokenClass"/>?
    /// </summary>
    /// <param name="method">Das Verb.</param>
    /// <param name="path">Der Pfad.</param>
    public static bool IsReachableOn1010(string method, string path) => Find(method, path)?.Reachable1010 ?? false;

    /// <summary>
    /// Die Token-Klasse aus der Sicherheitskonfiguration von 10.10: <c>general</c> (Login-Token),
    /// <c>module</c> (Modul-Token), <c>mixed</c>, <c>public</c>, <c>internal</c> oder
    /// <c>denied</c>; <c>null</c>, wenn die Operation nicht im Index steht.
    /// </summary>
    /// <param name="method">Das Verb.</param>
    /// <param name="path">Der Pfad.</param>
    public static string? TokenClass(string method, string path) => Find(method, path)?.TokenClass;

    /// <summary>Die Rollen, die die Operation verlangt (<c>USER</c>, <c>ERP</c>, <c>TANSS_APP</c>, …); leer, wenn unbekannt.</summary>
    /// <param name="method">Das Verb.</param>
    /// <param name="path">Der Pfad.</param>
    public static IReadOnlyCollection<string> Roles(string method, string path) => Find(method, path)?.Roles ?? [];

    /// <summary>Die Schlagworte der Spezifikation zur Operation; leer, wenn sie nicht im Index steht.</summary>
    /// <param name="method">Das Verb.</param>
    /// <param name="path">Der Pfad.</param>
    public static IReadOnlyCollection<string> Tags(string method, string path) => Find(method, path)?.Tags ?? [];

    /// <summary>Die <c>operationId</c> der kombinierten Spezifikation; <c>null</c>, wenn nicht im Index.</summary>
    /// <param name="method">Das Verb.</param>
    /// <param name="path">Der Pfad.</param>
    public static string? OperationId(string method, string path) => Find(method, path)?.OperationId;

    /// <summary>
    /// Bringt einen Pfad in die Schreibweise des Index: ohne Abfrage, ohne Schrägstrich am
    /// Ende, jeder Platzhalter als <c>{}</c>.
    /// </summary>
    /// <param name="path">Der Pfad.</param>
    public static string NormalisePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string bare = ApiPaths.StripQuery(path.Trim());
        if (bare.Length > 1)
        {
            bare = bare.TrimEnd('/');
        }

        return Placeholder().Replace(bare, "{}");
    }

    private static Operation? Find(string method, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Catalogue catalogue = Loaded.Value;
        string verb = method.Trim().ToUpperInvariant();
        string normalised = NormalisePath(path);

        if (catalogue.Operations.TryGetValue(Key(verb, normalised), out Operation? exact))
        {
            return exact;
        }

        // Konkrete Werte an Platzhalterstellen: /api/v1/tickets/4711 trifft {ticketId}.
        string[] segments = normalised.Split('/');
        foreach (Operation candidate in catalogue.Operations.Values)
        {
            if (candidate.Method == verb && SegmentsMatch(candidate.Segments, segments))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool SegmentsMatch(string[] pattern, string[] actual)
    {
        if (pattern.Length != actual.Length)
        {
            return false;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            bool placeholder = pattern[i] == "{}" && actual[i].Length > 0;
            if (!placeholder && !string.Equals(pattern[i], actual[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Key(string verb, string path) => verb + " " + path;

    private static Catalogue Load()
    {
        using Stream stream = typeof(ApiAvailability).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Die eingebettete Ressource \"{ResourceName}\" fehlt. Das Projekt Tanss.Api "
                + "bettet den Endpunkt-Index beim Bauen ein; ohne ihn ist die Bibliothek "
                + "unvollständig gebaut.");

        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        string server = ReadString(root, "serverVersion") ?? "?";

        int count = 0;
        Dictionary<string, Operation> operations = new(StringComparer.Ordinal);
        foreach (JsonElement entry in root.GetProperty("operations").EnumerateArray())
        {
            string? verb = ReadString(entry, "method")?.Trim().ToUpperInvariant();
            string? rawPath = ReadString(entry, "path");
            if (string.IsNullOrEmpty(verb) || string.IsNullOrEmpty(rawPath))
            {
                continue;
            }

            count++;
            string path = NormalisePath(rawPath);
            string tokenClass = ReadString(entry, "tokenClass") ?? string.Empty;
            bool in1010 = ReadFlag(entry, "in1010");
            Operation operation = new(
                verb,
                path,
                ReadString(entry, "operationId") ?? string.Empty,
                ReadStrings(entry, "tags"),
                tokenClass,
                ReadStrings(entry, "roles"),
                ReadFlag(entry, "documented"),
                in1010,
                in1010 && !string.Equals(tokenClass, DeniedTokenClass, StringComparison.Ordinal));

            // Erster Eintrag gewinnt: zwei Schluessel, die nur im Platzhalternamen oder im
            // Schraegstrich am Ende abweichen, meinen dieselbe Route.
            operations.TryAdd(Key(verb, path), operation);
        }

        return new Catalogue(server, count, operations);
    }

    private static bool ReadFlag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement flag) && flag.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] ReadStrings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                values.Add(text);
            }
        }

        return [.. values];
    }

    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    private sealed record Catalogue(string ServerVersion, int Count,
                                    Dictionary<string, Operation> Operations);

    private sealed record Operation(string Method, string Path, string OperationId, string[] Tags,
                                    string TokenClass, string[] Roles, bool Documented, bool In1010,
                                    bool Reachable1010)
    {
        public string[] Segments { get; } = Path.Split('/');
    }
}
