# TANSS Sharp API Client

**Ein .NET-Client für die TANSS-REST-API — vollständig, getippt und gegen eine echte Instanz gemessen.**

`Tanss.Api` deckt die gesamte REST-Oberfläche von TANSS ab: 1137 Endpunkte, jeder mit
typisierten Anfragen und Antworten. Dazu kommt das, was ein Client wirklich braucht, um mit
TANSS zu sprechen: die richtige Kopfzeile, die richtigen Token, die richtige Behandlung der
Eigenheiten, die in keiner Dokumentation stehen.

Kein Zwischendienst, keine Abhängigkeit zu einem Herstellerkonto. Die Bibliothek spricht
unmittelbar mit eurer Instanz.

> **Getestet gegen TANSS 10.10.** Alles, was unter [Gemessen, nicht geraten](#gemessen-nicht-geraten)
> steht, ist am 2026-09-15 lesend gegen eine produktive 10.10-Installation geprüft. Andere
> Versionen sollten funktionieren, sind aber nicht gemessen.

---

## Inhalt

- [Warum es diese Bibliothek gibt](#warum-es-diese-bibliothek-gibt)
- [Installation](#installation)
- [Erste Schritte](#erste-schritte)
- [Anmeldung](#anmeldung)
- [Token-Klassen und Rollen](#token-klassen-und-rollen)
- [Welche Route eure Instanz kennt](#welche-route-eure-instanz-kennt)
- [Fehlerbehandlung](#fehlerbehandlung)
- [Gemessen, nicht geraten](#gemessen-nicht-geraten)
- [Aufbau](#aufbau)
- [Bauen und testen](#bauen-und-testen)
- [Was die Bibliothek nicht tut](#was-die-bibliothek-nicht-tut)
- [Lizenz und Marken](#lizenz-und-marken)

---

## Warum es diese Bibliothek gibt

Wer TANSS über die Schnittstelle anspricht, schreibt dieselben drei Dinge immer wieder: die
Anmeldung, den Token-Umlauf und eine Handvoll Eigenheiten, die man erst nach dem dritten
unerklärlichen 403 kennt. Wir haben das dreimal getan — in einem Werkzeug für die
Zeiterfassung, einem für Fernwartungen und einem für Git-Commits — und danach genau einmal
richtig.

Diese Bibliothek ist das Ergebnis. Sie enthält **keinen Code aus unseren Anwendungen**: keine
Repositories, keine Abläufe, keine Annahmen über euren Betrieb. Nur den Client.

## Installation

```
dotnet add package Tanss.Api
```

Zielplattform ist `net10.0`, ohne Windows-Abhängigkeit. Läuft unter Windows, Linux und macOS.

## Erste Schritte

```csharp
using Tanss.Api;

TanssApiOptions optionen = new()
{
    // Die vollständige Basis der Schnittstelle. Eine TANSS-Installation liefert sie unter /backend aus.
    BaseUrl = new Uri("https://tanss.example.de/backend"),
    EmployeeId = 42,          // für den Parameter loggedInUserId, siehe unten
};

MutableTokenProvider token = new();
using TanssApi api = TanssApi.Create(optionen, token);

// Anmelden — das Zugangstoken landet automatisch im Token-Halter.
TanssLoginResult anmeldung = await api.Session.LoginAsync("benutzer", "kennwort");

// Ab hier steht die gesamte Schnittstelle typisiert zur Verfügung.
var meineTickets = await api.Rest.Api.V1.Tickets.Own.GetAsync();
var timer        = await api.Rest.Api.V1.Timers.GetAsync();

// Listen sind in TANSS oft ein PUT mit Filter — und trotzdem lesend.
var gefiltert = await api.Rest.Api.V1.Tickets.PutAsync(new TicketConfiguration
{
    Staff = [42],
    ItemsPerPage = 20,
    Page = 0,
});
```

Der Pfad im Code folgt dem Pfad in der Schnittstelle: `api.Rest.Api.V1.Tickets.Own` ist
`GET /api/v1/tickets/own`. Was TANSS kann, könnt ihr per Punkt erreichen.

## Anmeldung

Drei Wege, alle über `api.Session`:

```csharp
// 1. Benutzer und Kennwort
TanssLoginResult a = await api.Session.LoginAsync("benutzer", "kennwort");

// 2. Anwendungsspezifisches Anmeldetoken (zweiter Faktor)
TanssLoginResult b = await api.Session.LoginAsync("benutzer", password: null, loginToken: "123456");

// 3. Dashboard-Schlüssel: kein Benutzer, kein Kennwort, nur der Schlüssel,
//    den ihr in der TANSS-Administration beim Mitarbeiter hinterlegt.
TanssLoginResult c = await api.Session.LoginWithDashboardKeyAsync("euer-dashboard-schluessel");

// Erneuern, bevor das Zugangstoken abläuft (4 Stunden):
TanssLoginResult d = await api.Session.RefreshAsync(a.Refresh);

// Ein Token für ein externes Programm prägen (braucht das entsprechende Mitarbeiterrecht):
string appToken = await api.Session.MintAsync("tanss_app");
```

**Zwei Fallen, die die Bibliothek für euch abräumt:**

Eine **fehlgeschlagene Anmeldung antwortet mit HTTP 200**. Wer den Statuscode prüft, hält einen
Tippfehler im Kennwort für einen Erfolg. `LoginAsync` wirft in dem Fall eine
`TanssApiException` mit dem Grund aus `content.detailMessage`, etwa
`LOGIN_ERROR_INVALID_USERNAME_OR_PASSWORD`.

**Zugangsdaten reisen in Kopfzeilen, und zwar als UTF-8.** Der Server liest sie als
Latin-1-Bytes und deutet sie anschließend als UTF-8. .NET schreibt Kopfzeilen dagegen als ASCII
und weist alles darüber ab. Die Bibliothek kodiert deshalb selbst und bringt eine
Verbindungsschicht mit, die diese Bytes auch schreiben darf — ein Kennwort mit Umlauten kommt
an. (Ein Kennwort mit `€` nicht: Der Server schreibt das Zeichen auf U+0080 um. Das ist eine
Grenze des Servers, keine der Bibliothek.)

## Token-Klassen und Rollen

TANSS kennt nicht ein Token, sondern mehrere. Welche Route welches verlangt, ist im Server fest
verdrahtet — und die Bibliothek weiß es:

```csharp
TokenRoles.RequiredFor("/api/v1/tickets/own");   // User    — das Anmeldetoken genügt
TokenRoles.RequiredFor("/api/erp/v1/customers"); // ErpOrCentron — eigenes ERP-Token nötig
TokenRoles.RequiredFor("/api/v1/login");         // None    — ohne Token erreichbar
TokenRoles.RequiredFor("/api/v1/irgendwas");     // Denied  — diese Route gibt es nicht

TokenRoles.IsReachableWithLoginToken("/api/calls/v1"); // false
```

| Klasse | Bedeutung |
|---|---|
| `general` | Das Anmeldetoken des Mitarbeiters genügt. |
| `module` | Eigenes Token je Anbindung, in der TANSS-Administration erzeugt: ERP, Telefonie, Fernwartung, Monitoring, Stempeluhr, Gerätemanagement, TANSS-App und weitere. |
| `mixed` | Anmeldetoken **oder** ein weiteres Token. |
| `public` | Ohne Token erreichbar. |
| `denied` | Kein Token der Welt öffnet diese Route. |

Wer es vorher wissen will, spart sich das 403: TANSS beantwortet jede Ablehnung mit leerem
Körper, und ein fehlendes Recht sieht dabei genauso aus wie ein abgelaufenes Token.

**`loggedInUserId`:** Ein Anmeldetoken braucht den Parameter nicht, ein App-Token schon — ohne
ihn bekommt es die Benutzerrolle nicht. Die Bibliothek hängt ihn genau dort an, wo er wirkt,
sofern ihr `EmployeeId` gesetzt habt. Ihr müsst euch darum nicht kümmern.

## Welche Route eure Instanz kennt

Nicht jede erzeugte Operation existiert in jeder TANSS-Version, und manche sind in der
Sicherheitskonfiguration des Servers gar nicht freigeschaltet. `ApiAvailability` beantwortet das,
ohne dass ihr es ausprobieren müsst:

```csharp
ApiAvailability.IsKnown("GET", "/api/v1/todos");            // gehört die Route zum Katalog?
ApiAvailability.IsAvailableOn1010("GET", "/api/v1/todos");  // gibt es sie auf 10.10?
ApiAvailability.IsReachableOn1010("GET", "/api/v1/sla");    // false: auf 10.10 gesperrt
ApiAvailability.TokenClass("GET", "/api/erp/v1/customers"); // "module"
ApiAvailability.Tags("GET", "/api/v1/tickets/own");         // ["tickets"]
```

## Fehlerbehandlung

```csharp
try
{
    var ticket = await api.Rest.Api.V1.Tickets[99999999].GetAsync();
}
catch (TanssApiException ex)
{
    ex.StatusCode;    // 404
    ex.ErrorCode;     // "OBJECT_NOT_FOUND" — stabil, für den Programmablauf
    ex.LocalizedText; // "Das Objekt wurde nicht gefunden" — für den Menschen
    ex.TraceId;       // zum Nachschlagen im Server-Protokoll
}
```

TANSS antwortet in zwei Formen: der fachlichen Hülle mit `error.text`, `error.localizedText`,
`error.type` und `error.traceId` — und, wenn schon das Rahmenwerk ablehnt, einem
RFC-7807-Dokument. `TanssApiException` liest beide.

## Gemessen, nicht geraten

Am 2026-09-15 lesend gegen eine produktive **TANSS 10.10** geprüft. Diese Punkte weichen von dem
ab, was man in der Dokumentation erwartet — deshalb stehen sie hier:

| Sache | Befund |
|---|---|
| Anmelderoute | `POST /api/v1/user/login`, Zugangsdaten in den Kopfzeilen `user`, `password` bzw. `logintoken` oder `dbapikey` |
| Fehlgeschlagene Anmeldung | HTTP **200** mit `content.detailMessage`; der Statuscode taugt nicht zur Prüfung |
| Erneuerung | Kopfzeile `refreshToken`, **ohne** `apiToken`, auf einer beliebigen Nicht-Anmelderoute |
| Token-Kopfzeile | `apiToken`, Präfix `Bearer ` ist Pflicht; `Authorization` wird ignoriert |
| Ablehnungen | 403 mit **leerem** Körper |
| `loggedInUserId` | beim Anmeldetoken wirkungslos, selbst ein falscher Wert ändert nichts |
| Modul-Präfixe | mit Anmeldetoken durchweg 403 |
| Gesperrte Routen | Die Sicherheitskonfiguration zählt die freigegebenen Bereiche einzeln auf und sperrt den Rest |
| Fehlerhülle | enthält `traceId` |
| Zugangsdaten-Kodierung | Kopfzeilen werden als UTF-8 gedeutet |

Zehn lesende Live-Tests halten diese Befunde fest. Sie laufen nur, wenn ihr sie mit euren
Zugangsdaten startet, und sie schreiben nichts:

```
TANSS_BASE_URL=https://tanss.example.de/backend TANSS_USER=… TANSS_PASSWORD=… \
  dotnet test tests/Tanss.Api.Live.Tests
```

## Aufbau

| Ort | Inhalt |
|---|---|
| `src/Tanss.Api` | Die Bibliothek: Einstieg `TanssApi`, `TanssSession`, `TanssApiOptions`, `TanssApiException`, `TokenRoles`, `ApiAvailability` |
| `src/Tanss.Api/Rest` | Verbindungsschicht (Token-Kopfzeile, `loggedInUserId`, Wiederholung nur bei lesenden Aufrufen) und der erzeugte Client |
| `tests/Tanss.Api.Tests` | Tests ohne Netz, darunter zwei, die die Kodierung der Zugangsdaten an einer echten Verbindung nachweisen |
| `tests/Tanss.Api.Live.Tests` | Lesende Tests gegen eine echte Instanz, nur mit gesetzten Umgebungsvariablen |

Der erzeugte Teil des Clients entsteht aus der offiziellen Schnittstellenbeschreibung; diese ist
nicht Teil dieses Repositorys.

Die Wiederholung nach einem Fehler gilt ausschließlich für lesende Aufrufe und nur bei 429, 503
und 504. Schreibende Aufrufe gehen genau einmal auf die Leitung: **TANSS dedupliziert nicht**,
eine zweite gleiche Buchung erzeugt einen zweiten Datensatz.

## Bauen und testen

```
dotnet build   Tanss.Api.slnx -c Release -warnaserror
dotnet test    tests/Tanss.Api.Tests
```

## Was die Bibliothek nicht tut

- **Sie entscheidet nichts fachlich.** Keine Buchungslogik, keine Dublettenprüfung, keine
  Annahmen über euren Betrieb. Das gehört in eure Anwendung.
- **Sie hält keine Sitzung am Leben.** Wann ihr erneuert, entscheidet ihr.
- **Sie verschleiert keine Rechte.** Was der angemeldete Benutzer nicht darf, darf auch die
  Bibliothek nicht.

## Lizenz und Marken

Der Quelltext dieses Projekts steht unter der MIT-Lizenz (siehe `LICENSE`).

TANSS ist ein Produkt der HUCK IT GmbH. Dieses Projekt ist ein unabhängiger Client, steht in
keiner Verbindung zur HUCK IT GmbH und wird von ihr weder unterstützt noch geprüft. Die
Schnittstelle selbst, ihre Dokumentation und alle Marken gehören ihren jeweiligen Inhabern; die
Nennung dient allein dazu, zu sagen, wofür dieser Client gemacht ist. Für den Einsatz braucht ihr
eine eigene TANSS-Installation und die nötigen Rechte darin.

---

[ProNet Systems GmbH](https://www.pronet-systems.de) · IT-Systemhaus in Arnsberg
