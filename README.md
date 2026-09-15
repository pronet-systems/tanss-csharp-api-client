# TANSS Sharp API Client

**Ein .NET-Client für die TANSS-REST-API - Getestet gegen TANSS 10.10.**

---

1137 Endpunkte, typisiert, mit Anmeldung, Token-Verwaltung und den Eigenheiten der Schnittstelle.
Kein Zwischendienst: Die Bibliothek spricht unmittelbar mit eurer Instanz.

## Installation

```
dotnet add package Tanss.Api
```

Zielplattform `net10.0`, ohne Windows-Abhängigkeit.

## Beispiel

```csharp
using Tanss.Api;

TanssApiOptions optionen = new()
{
    // Vollständige Basis der Schnittstelle; eine TANSS-Installation liefert sie unter /backend aus.
    BaseUrl = new Uri("https://tanss.example.de/backend"),
    EmployeeId = 42,
};

MutableTokenProvider token = new();
using TanssApi api = TanssApi.Create(optionen, token);

// Anmelden — das Zugangstoken landet automatisch im Token-Halter.
await api.Session.LoginAsync("benutzer", "kennwort");

// Ab hier steht die gesamte Schnittstelle bereit: Der Pfad im Code ist der Pfad in der API.
var meineTickets = await api.Rest.Api.V1.Tickets.Own.GetAsync();   // GET /api/v1/tickets/own

// Listen sind in TANSS oft ein PUT mit Filter — und trotzdem lesend.
var gefiltert = await api.Rest.Api.V1.Tickets.PutAsync(new TicketConfiguration
{
    Staff = [42],
    ItemsPerPage = 20,
});
```

Weitere Einstiege: `api.Session.LoginWithDashboardKeyAsync(…)` und
`api.Session.LoginAsync(benutzer, password: null, loginToken: …)` für die beiden anderen
Anmeldewege, `api.Session.RefreshAsync(…)` zum Erneuern, `api.Session.MintAsync("tanss_app")` für
ein Token für ein externes Programm.

## Was die Bibliothek für euch erledigt

- **Anmeldung.** Alle drei Wege. Eine fehlgeschlagene Anmeldung antwortet mit HTTP 200 — wer den
  Statuscode prüft, hält einen Tippfehler für einen Erfolg. Hier gibt es eine `TanssApiException`
  mit dem Grund. Zugangsdaten mit Umlauten kommen richtig an.
- **Token.** Kopfzeile `apiToken` mit `Bearer`-Präfix, der Parameter `loggedInUserId` genau dort,
  wo er wirkt. `TokenRoles.RequiredFor(pfad)` sagt vorher, ob euer Token die Route überhaupt
  erreicht — TANSS beantwortet jede Ablehnung mit leerem Körper.
- **Verfügbarkeit.** `ApiAvailability` sagt, welche Route eure Version kennt und welche der
  Server gesperrt hat.
- **Fehler.** `TanssApiException` mit `ErrorCode`, `LocalizedText` und `TraceId`.
- **Sicherheit beim Schreiben.** Wiederholt wird nur bei lesenden Aufrufen. Schreibende gehen
  genau einmal auf die Leitung: TANSS dedupliziert nicht.

## Bauen und testen

```
dotnet build Tanss.Api.slnx -c Release -warnaserror
dotnet test  tests/Tanss.Api.Tests

# Lesende Tests gegen eine echte Instanz; ohne diese Variablen werden sie übersprungen.
TANSS_BASE_URL=https://tanss.example.de/backend TANSS_USER=… TANSS_PASSWORD=… \
  dotnet test tests/Tanss.Api.Live.Tests
```

Der erzeugte Teil unter `src/Tanss.Api/Rest/Generated` entsteht aus der offiziellen
Schnittstellenbeschreibung; sie ist nicht Teil dieses Repositorys.

## Lizenz und Marken

Der Quelltext steht unter der MIT-Lizenz (siehe `LICENSE`).

TANSS ist ein Produkt der HUCK IT GmbH. Dieses Projekt ist ein unabhängiger Client, steht in
keiner Verbindung zur HUCK IT GmbH und wird von ihr weder unterstützt noch geprüft. Marken
gehören ihren jeweiligen Inhabern; die Nennung dient allein dazu, zu sagen, wofür dieser Client
gemacht ist.

---

[ProNet Systems GmbH](https://www.pronet-systems.de) · IT-Systemhaus in Arnsberg
