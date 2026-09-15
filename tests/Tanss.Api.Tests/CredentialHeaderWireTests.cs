using System.Net;
using System.Net.Sockets;
using System.Text;
using Tanss.Api.Rest;
using Xunit;

namespace Tanss.Api.Tests;

/// <summary>
/// Beweist an einer echten Verbindung, dass Zugangsdaten mit Umlauten unveraendert auf der Leitung
/// ankommen.
/// </summary>
/// <remarks>
/// <para>Der Server liest die Kopfzeilen der Anmeldung als Latin-1-Bytes und deutet sie anschliessend als
/// UTF-8 (vom Server so umgesetzt, gemessen gegen TANSS 10.10 am 2026-09-15, siehe README, Abschnitt
/// „Gemessen, nicht geraten“). <see cref="TanssSession"/> legt deshalb die UTF-8-Bytes als
/// Latin-1-Zeichenkette in die Kopfzeile, und <see cref="TanssRestClientFactory.CreateFinalHandler"/>
/// waehlt Latin-1 als Kodierung der Kopfzeilen — sonst wiese .NET jedes Zeichen ueber U+007F mit
/// <c>Request headers must contain only ASCII characters</c> ab.</para>
/// <para>Der Test haengt an keinem fremden Rechner: Er hoert selbst auf einem Anschluss der
/// Rueckschleife, liest die rohen Bytes und antwortet mit einer festen Anmeldeantwort.</para>
/// </remarks>
public sealed class CredentialHeaderWireTests
{
    private const string LoginResponse =
        """{"meta":{"text":"Welcome, your ApiToken is 4 hours valid."},"content":{"employeeId":7,"apiKey":"Bearer abc","expire":1789477324,"refresh":"Bearer def","employeeType":"TECHNICAN"}}""";

    [Fact]
    public async Task Ein_Kennwort_mit_Umlauten_kommt_als_UTF_8_auf_der_Leitung_an()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task<byte[]> request = AcceptOnceAsync(listener);

        TanssApiOptions options = new() { BaseUrl = new Uri($"http://127.0.0.1:{port}/backend"), EmployeeId = 7 };
        MutableTokenProvider tokens = new();
        using TanssApi api = TanssApi.Create(options, tokens);

        TanssLoginResult result = await api.Session.LoginAsync("müller", "Paßwortäöü");
        byte[] raw = await request;

        Assert.Equal(7, result.EmployeeId);

        // Die Kopfzeilen muessen die UTF-8-Bytes tragen: ae = C3 A4, ss = C3 9F, ue = C3 BC.
        Assert.Contains(Concat("user: ", "müller"u8), raw);
        Assert.Contains(Concat("password: ", "Paßwortäöü"u8), raw);

        // Und eben nicht die Latin-1-Bytes (ae = E4), die ein unbedachter Client schicken wuerde.
        Assert.DoesNotContain(Concat("user: ", Encoding.Latin1.GetBytes("müller")), raw);
    }

    [Fact]
    public async Task Auch_der_Dashboard_Schluessel_reist_unveraendert()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task<byte[]> request = AcceptOnceAsync(listener);

        TanssApiOptions options = new() { BaseUrl = new Uri($"http://127.0.0.1:{port}/backend") };
        using TanssApi api = TanssApi.Create(options, new MutableTokenProvider());

        await api.Session.LoginWithDashboardKeyAsync("schlüssel-äöü");
        byte[] raw = await request;

        Assert.Contains(Concat("dbapikey: ", "schlüssel-äöü"u8), raw);

        // Der Dashboard-Weg schickt weder Benutzer noch Kennwort (gemessen gegen TANSS 10.10 am 2026-09-15).
        string text = Encoding.Latin1.GetString(raw);
        Assert.DoesNotContain("password:", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\r\nuser:", text, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Concat(string name, ReadOnlySpan<byte> value)
    {
        byte[] prefix = Encoding.ASCII.GetBytes(name);
        byte[] all = new byte[prefix.Length + value.Length];
        prefix.CopyTo(all, 0);
        value.CopyTo(all.AsSpan(prefix.Length));
        return all;
    }

    /// <summary>Nimmt eine Verbindung an, liest den Kopf der Anfrage und antwortet mit der Anmeldeantwort.</summary>
    private static async Task<byte[]> AcceptOnceAsync(TcpListener listener)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        using NetworkStream stream = client.GetStream();

        byte[] buffer = new byte[8192];
        int read = 0;
        while (read < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(read));
            read += count;
            if (count == 0) break;

            // Der Kopf endet mit einer Leerzeile; ein Rumpf kommt bei der Anmeldung nicht.
            if (Encoding.Latin1.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
        }

        byte[] body = Encoding.UTF8.GetBytes(LoginResponse);
        byte[] head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
        await stream.FlushAsync();

        return buffer[..read];
    }
}
