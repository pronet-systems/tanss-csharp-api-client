namespace Tanss.Api.Tests.Fakes;

/// <summary>Ein Token-Anbieter für Tests: zählt, wie oft gefragt wurde.</summary>
internal sealed class FakeTokenProvider : ITanssTokenProvider
{
    private readonly Func<int, string?> _tokenFor;
    private int _reads;

    public FakeTokenProvider(string? token) => _tokenFor = _ => token;

    /// <summary>Liefert bei jedem Lesen ein anderes Token — für die Rotationsprobe.</summary>
    public FakeTokenProvider(Func<int, string?> tokenFor) => _tokenFor = tokenFor;

    public int Reads => Volatile.Read(ref _reads);

    public string? GetToken() => _tokenFor(Interlocked.Increment(ref _reads));
}

/// <summary>Kleine Helfer, damit die Tests von der Einrichtung nicht erdrückt werden.</summary>
internal static class TestEnvironment
{
    public const string BaseUrl = "https://tanss.example.invalid/backend";
    public const int EmployeeId = 42;
    public const string Token = "Bearer test.token.value";

    /// <summary>Der kleinste gültige Erfolgsumschlag: leeres <c>meta</c>, leere Liste.</summary>
    public const string EmptyList = """{"meta":{},"content":[]}""";

    public static TanssApiOptions Options(bool validateTls = true, int? employeeId = EmployeeId) => new()
    {
        BaseUrl = new Uri(BaseUrl),
        EmployeeId = employeeId,
        Timeout = TimeSpan.FromSeconds(5),
        ValidateTls = validateTls,
    };
}
