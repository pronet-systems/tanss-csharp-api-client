namespace Tanss.Api;

/// <summary>
/// Alles, was der Zugang zu einer TANSS-Instanz an Einstellungen braucht: Adresse, Mitarbeiter,
/// Zeitgrenze, Proxy, TLS-Prüfung.
/// </summary>
/// <remarks>
/// Die Klasse trägt nur Werte; geprüft und angewandt werden sie beim Bau des Clients
/// (<see cref="Rest.TanssRestClientFactory"/>).
/// </remarks>
public sealed record TanssApiOptions
{
    /// <summary>
    /// Die Basisadresse, der jeder Pfad der Spezifikation angehängt wird.
    /// </summary>
    /// <remarks>
    /// <para>Die Spezifikation trägt als einzigen Server-Eintrag
    /// <c>https://{host}</c> mit der Variablen <c>host</c> („Hostname of your TANSS instance“;
    /// Spezifikation, Abschnitt <c>servers</c>). Sie sagt damit <b>nicht</b>, unter welchem Pfad
    /// die Schnittstelle einer Installation liegt.</para>
    /// <para>Auf einer TANSS-Installation wird die Schnittstelle unter <c>/backend</c>
    /// ausgeliefert; die Weboberfläche liegt daneben unter dem Stammpfad. Die Bibliothek stellt
    /// deshalb nichts von sich aus voran, sondern erwartet die <b>vollständige</b> Basis
    /// einschließlich <c>/backend</c>, etwa <c>https://tanss.example.de/backend</c>. Wer statt
    /// dessen die Oberfläche angibt, bekommt deren Antworten und nicht die der Schnittstelle.</para>
    /// <para>Verlangt wird eine absolute <c>http</c>- oder <c>https</c>-Adresse; ein
    /// Schrägstrich am Ende ist gleichgültig.</para>
    /// </remarks>
    public required Uri BaseUrl { get; init; }

    /// <summary>
    /// Die Mitarbeiter-Id, die als Abfrageparameter <c>loggedInUserId</c> mitgeschickt wird; <c>null</c>,
    /// wenn keine bekannt ist.
    /// </summary>
    /// <remarks>
    /// <para>Der Parameter ist kein Zierrat: Der Server gibt einem Token vom Typ <c>TANSS_APP</c> die
    /// Rolle <c>ROLE_USER</c> nur dann, wenn <c>loggedInUserId</c> in der Anfrage steht — ohne ihn fehlt
    /// der Mitarbeiterkontext und die Route antwortet 403. Ein Login-Token (<c>ACCESS</c>) trägt den
    /// Mitarbeiter bereits im Claim <c>sub</c>; der Parameter stört dort nicht.</para>
    /// <para>Ist der Wert <c>null</c>, hängt <see cref="Rest.LoggedInUserIdHandler"/> nichts an. Das ist
    /// richtig für Modul-Token (ERP, PHONE, REMOTE_SUPPORT, …), die ohnehin keinen Mitarbeiter kennen.
    /// Die Id liefert <c>POST /api/v1/login</c> im Feld <c>content.employeeId</c> (Spezifikation,
    /// Beispiel der 200-Antwort).</para>
    /// </remarks>
    public int? EmployeeId { get; init; }

    /// <summary>Zeitgrenze einer einzelnen Anfrage; Vorgabe 30 Sekunden.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Vorgeschalteter Proxy, etwa <c>http://proxy.example.de:3128</c>; <c>null</c> für den Direktweg.</summary>
    public Uri? Proxy { get; init; }

    /// <summary>Benutzername für den Proxy; <c>null</c>, wenn er keine Anmeldung verlangt.</summary>
    public string? ProxyUser { get; init; }

    /// <summary>Kennwort für den Proxy.</summary>
    public string? ProxyPassword { get; init; }

    /// <summary>
    /// Prüft die Bibliothek das TLS-Zertifikat der Instanz? Vorgabe <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <b>Warnung:</b> <c>false</c> schaltet die Zertifikatsprüfung vollständig ab. Die
    /// Verbindung ist dann gegen einen Angreifer in der Mitte wertlos: Wer den Netzweg
    /// kontrolliert, liest Token und Daten mit und kann Antworten fälschen. Vertretbar
    /// allenfalls für eine Instanz im eigenen Netz mit selbstsigniertem Zertifikat, und auch
    /// dort nur, bis das Zertifikat im Rechnerspeicher hinterlegt ist.
    /// </remarks>
    public bool ValidateTls { get; init; } = true;
}
