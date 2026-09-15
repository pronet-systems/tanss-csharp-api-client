using System.Net;
using System.Text;

namespace Tanss.Api.Tests.Fakes;

/// <summary>
/// Eine Attrappe der Verbindungsschicht: hält jede Anfrage fest und antwortet nach Vorgabe.
/// </summary>
/// <remarks>
/// <para>Der Rumpf wird sofort ausgelesen und mitgeschrieben — nach dem Senden gibt der Client
/// die Anfrage frei, und ein später gelesener Rumpf wäre leer.</para>
/// <para>Die Antwort trägt ihre Anfrage (<see cref="HttpResponseMessage.RequestMessage"/>),
/// wie es eine echte Verbindungsschicht auch tut. Kiotas Wiederholungs-Handler reicht die
/// Antwort an die Wiederholungsregel weiter, und die liest Verb und Pfad genau dort.</para>
/// </remarks>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<CapturedRequest, HttpResponseMessage> _respond;
    private readonly List<CapturedRequest> _captured = [];
    private readonly Lock _gate = new();
    private int _calls;

    public RecordingHandler(Func<CapturedRequest, HttpResponseMessage> respond) => _respond = respond;

    /// <summary>Antwortet immer gleich.</summary>
    public RecordingHandler(HttpStatusCode status, string body)
        : this(_ => Respond(status, body))
    {
    }

    /// <summary>Wie oft wurde gesendet? Zählt auch Versuche, die in einer Ausnahme endeten.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public IReadOnlyList<CapturedRequest> Captured
    {
        get
        {
            lock (_gate)
            {
                return [.. _captured];
            }
        }
    }

    public CapturedRequest Last
    {
        get
        {
            lock (_gate)
            {
                return _captured[^1];
            }
        }
    }

    public static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                 CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        int attempt = Interlocked.Increment(ref _calls);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        CapturedRequest captured = new(
            request.Method.Method,
            request.RequestUri ?? new Uri("about:blank"),
            headers.TryGetValue("apiToken", out string? token) ? token : null,
            request.Headers.Contains("Authorization"),
            body,
            attempt,
            headers);

        lock (_gate)
        {
            _captured.Add(captured);
        }

        HttpResponseMessage response = _respond(captured);
        response.RequestMessage = request;
        return response;
    }
}

/// <summary>Eine festgehaltene Anfrage.</summary>
/// <param name="Method">HTTP-Verb.</param>
/// <param name="Uri">Vollständige Adresse einschließlich Abfragezeichenkette.</param>
/// <param name="ApiToken">Wert der Kopfzeile <c>apiToken</c>, falls gesetzt.</param>
/// <param name="HasAuthorizationHeader">
/// War eine Kopfzeile <c>Authorization</c> gesetzt? TANSS erwartet sie nicht, also darf sie
/// niemals auftauchen.
///</param>
/// <param name="Body">Der gesendete Rumpf als Text.</param>
/// <param name="Attempt">Der wievielte Sendeversuch dies war, beginnend bei 1.</param>
/// <param name="Headers">
/// Alle Kopfzeilen der Anfrage, ohne Ruecksicht auf Gross- und Kleinschreibung — die Anmeldung
/// reist in ihnen (user, password, logintoken), die Erneuerung ebenso (refreshToken).
///</param>
internal sealed record CapturedRequest(string Method, Uri Uri, string? ApiToken,
                                       bool HasAuthorizationHeader, string? Body, int Attempt,
                                       IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>Der Wert einer Kopfzeile; <c>null</c>, wenn sie fehlt.</summary>
    public string? Header(string name) => Headers.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Stand diese Kopfzeile ueberhaupt in der Anfrage?</summary>
    public bool HasHeader(string name) => Headers.ContainsKey(name);

    /// <summary>Liest einen Abfrageparameter; <c>null</c>, wenn er fehlt.</summary>
    public string? Query(string name)
    {
        foreach (string pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int split = pair.IndexOf('=', StringComparison.Ordinal);
            string key = split < 0 ? pair : pair[..split];
            if (string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
            {
                return split < 0 ? string.Empty : Uri.UnescapeDataString(pair[(split + 1)..]);
            }
        }

        return null;
    }
}
