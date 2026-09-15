namespace Tanss.Api;

/// <summary>
/// Die Rolle, die ein Token tragen muss, damit TANSS eine Route überhaupt an den Controller durchreicht.
/// </summary>
/// <remarks>
/// Aus der Sicherheitskonfiguration des Servers, gegen TANSS 10.10 geprüft. Die Rolle entscheidet nur die
/// erste Hürde; ob der Mitarbeiter das Ticket dann auch sehen darf, prüft TANSS je Route über seine
/// Rechte.
/// </remarks>
public enum TokenRole
{
    /// <summary><c>denyAll</c>: Kein Token der Welt öffnet diese Route.</summary>
    Denied = 0,

    /// <summary>Frei: Die Route braucht kein Token — die Anmeldung selbst und ein paar öffentliche Pfade.</summary>
    None,

    /// <summary>Das Login-Token des Technikers aus <c>POST /api/v1/login</c>.</summary>
    User,

    /// <summary>ERP-Anbindung Systemhaus.ONE, eigenes Token.</summary>
    SystemhausOne,

    /// <summary>ERP-Token, Rolle <c>ERP</c> oder <c>CENTRON</c>.</summary>
    ErpOrCentron,

    /// <summary>TANSS-X-Plattform (COERO GmbH).</summary>
    Coero,

    /// <summary>Telefonanlagen-Anbindung.</summary>
    Phone,

    /// <summary>Fernwartungs-Import, Praefix <c>/api/remoteSupports/v1</c>.</summary>
    RemoteSupport,

    /// <summary>Monitoring-Anbindung.</summary>
    Monitoring,

    /// <summary>App- und TANSS-X-Token, geprägt über <c>GET /api/v1/jwts/tanss_app</c>.</summary>
    TanssApp,

    /// <summary>Stempeluhr-Anbindung.</summary>
    Timestamp,

    /// <summary>Gerätemanagement-Anbindung.</summary>
    DeviceManagement,

    /// <summary>Server-Eye.</summary>
    Servereye,

    /// <summary>Angebote: Login-Token oder Angebots-Token.</summary>
    UserOrOffer,

    /// <summary>Intern (PHP-Oberfläche): Login-Token oder PHP-Token.</summary>
    UserOrPhp,

    /// <summary>Sprachen: Login-Token oder eines der beiden Landingpage-Token.</summary>
    UserOrLandingPage,

    /// <summary>Kundenportal-Landingpage für Ticket-Abläufe.</summary>
    LandingPageTicketWorkflow,

    /// <summary>Kundenportal-Landingpage für Vertrags-Abläufe.</summary>
    LandingPageContractWorkflow,

    /// <summary>Spring-Betriebsdaten unter <c>/actuator</c>.</summary>
    Actuator,
}

/// <summary>Welche Rolle ein Pfad verlangt — die Präfix-Tabelle der TanssApi 10.10.0.</summary>
/// <remarks>
/// <para><b>Die erste Übereinstimmung gewinnt</b>, in der Reihenfolge der Tabelle, so wie Spring Security
/// sie auswertet. <c>/api/v1/offers</c> steht deshalb vor der Sammelregel für <c>/api/v1/**</c>, und
/// <c>/api/v1/login</c> ganz oben.</para>
/// <para><b>Ein Login-Token erreicht nur die USER-Präfixe.</b> Das Sitzungstoken aus <c>POST
/// /api/v1/login</c> trägt die Rolle <c>USER</c> und öffnet damit <c>/api/v1/**</c> — sonst nichts.
/// <c>/api/erp/v1</c>, <c>/api/tanss.x/v1</c>, <c>/api/remoteSupports/v1</c> und alle anderen Präfixe
/// verlangen ein je Modul geprägtes Token mit der jeweiligen Rolle; mit dem Login-Token antworten sie
/// 403, und die Meldung sieht genauso aus wie bei einem abgelaufenen Token. Wer eine solche Route
/// aufrufen will, prüft vorher mit <see cref="IsReachableWithLoginToken"/> — oder beschafft das passende
/// Token, für <c>tanss.x</c> etwa über <c>GET /api/v1/jwts/tanss_app</c>.</para>
/// <para>Es gibt keine Sammelregel für <c>/api/v1/**</c>: TANSS zählt die USER-Module einzeln auf und
/// sperrt alles Übrige mit <c>denyAll</c>. Ein <c>/api/v1</c>-Pfad, dessen Modul in keiner Zeile steht
/// (in 10.10 z. B. <c>sla</c>, <c>priorities</c>, <c>vouchers</c>, <c>filesAndLinks</c>), ist
/// <see cref="TokenRole.Denied"/>, auch wenn die offizielle Schnittstellenbeschreibung ihn kennt. Ob die
/// Instanz eine erlaubte Route dann kennt, sagt <see cref="ApiAvailability"/>.</para>
/// </remarks>
public static class TokenRoles
{
    private static readonly Rule[] Table =
    [
        // Die Regeln in der Auswertungsreihenfolge des Servers (vom Server so umgesetzt, gegen 10.10
        // geprueft). Die Reihenfolge ist Teil der Regel: Es gilt der erste Treffer.
        new("/api/v1/login", TokenRole.None, Exact: true),
        new("/api/systemhaus_one/v1", TokenRole.SystemhausOne),
        new("/api/erp/v1", TokenRole.ErpOrCentron),
        new("/api/coero/v1", TokenRole.Coero),
        new("/api/calls/v1", TokenRole.Phone),
        new("/api/remoteSupports/v1", TokenRole.RemoteSupport),
        new("/api/monitoring/v1", TokenRole.Monitoring),
        new("/api/tanss.app/v1", TokenRole.TanssApp),
        new("/api/tanss.x/v1", TokenRole.TanssApp),
        new("/api/timestamps/v1", TokenRole.Timestamp),
        new("/api/deviceManagement/v1", TokenRole.DeviceManagement),
        new("/api/servereye/v1", TokenRole.Servereye),
        new("/api/v1/tickets", TokenRole.User),
        new("/api/v1/checklists", TokenRole.User),
        new("/api/v1/checklistItems", TokenRole.User),
        new("/api/v1/checklistEvents", TokenRole.User),
        new("/api/v1/timers", TokenRole.User),
        new("/api/v1/supports", TokenRole.User),
        new("/api/v1/todos", TokenRole.User),
        new("/api/v1/tasks", TokenRole.User),
        new("/api/v1/employees", TokenRole.User),
        new("/api/v1/telephoneSystems", TokenRole.User),
        new("/api/v1/util/files", TokenRole.None),
        new("/api/v1/util/languages", TokenRole.UserOrLandingPage),
        new("/api/v1/util", TokenRole.User),
        new("/api/v1/cloud/isTokenValid", TokenRole.None),
        new("/api/v1/cloud/upload", TokenRole.None),
        new("/api/v1/cloud", TokenRole.User),
        new("/.well-known/jwks.json", TokenRole.None, Exact: true),
        new("/api/v1/companies", TokenRole.User),
        new("/api/v1/projects", TokenRole.User),
        new("/api/v1/supports", TokenRole.User),
        new("/api/v1/timeline", TokenRole.User),
        new("/api/v1/cache", TokenRole.UserOrPhp),
        new("/api/v1/roles", TokenRole.User),
        new("/api/v1/ev", TokenRole.User),
        new("/api/v1/test", TokenRole.User),
        new("/api/v1/mails", TokenRole.User),
        new("/api/v1/chats", TokenRole.User),
        new("/api/v1/jwts", TokenRole.User),
        new("/api/v1/templates", TokenRole.User),
        new("/api/v1/availability", TokenRole.User),
        new("/api/v1/ws", TokenRole.None),
        new("/api/v1/offers", TokenRole.UserOrOffer),
        new("/api/v1/tags", TokenRole.User),
        new("/api/v1/sysTasks", TokenRole.User),
        new("/api/v1/callbacks", TokenRole.User),
        new("/api/v1/search", TokenRole.User),
        new("/api/v1/ticketBoard", TokenRole.User),
        new("/actuator", TokenRole.Actuator),
        new("/api/v1/timestamps", TokenRole.User),
        new("/api/v1/externals", TokenRole.User),
        new("/api/v1/planning", TokenRole.User),
        new("/api/v1/tmpFileUploads", TokenRole.User),
        new("/api/v1/remoteSupports", TokenRole.User),
        new("/api/v1/pcs", TokenRole.User),
        new("/api/v1/peripheries", TokenRole.User),
        new("/api/v1/components", TokenRole.User),
        new("/api/v1/tanssEvents", TokenRole.User),
        new("/api/v1/log", TokenRole.User),
        new("/api/v1/paymentMethods", TokenRole.User),
        new("/api/v1/push", TokenRole.User),
        new("/api/v1/admin", TokenRole.User),
        new("/api/v1/escalations", TokenRole.User),
        new("/api/v1/textModules", TokenRole.User),
        new("/api/v1/tanssLicenses", TokenRole.User),
        new("/api/v1/qr", TokenRole.User),
        new("/api/v1/cars", TokenRole.User),
        new("/api/v1/recurrence", TokenRole.User),
        new("/api/v1/os", TokenRole.User),
        new("/api/v1/manufacturers", TokenRole.User),
        new("/api/v1/cpus", TokenRole.User),
        new("/api/v1/hddTypes", TokenRole.User),
        new("/api/v1/services", TokenRole.User),
        new("/api/v1/systemhaus_one", TokenRole.User),
        new("/api/v1/erp", TokenRole.User),
        new("/api/v1/identify", TokenRole.User),
        new("/api/v1/domains", TokenRole.User),
        new("/api/v1/managementDashboard", TokenRole.User),
        new("/api/v1/genericAssignments", TokenRole.User),
        new("/api/v1/holidays", TokenRole.User),
        new("/api/v1/mass", TokenRole.User),
        new("/api/v1/emailSettings", TokenRole.User),
        new("/api/v1/mailRobot", TokenRole.User),
        new("/api/v1/companyCategories", TokenRole.User),
        new("/api/v1/vacationRequests", TokenRole.User),
        new("/api/v1/overtime", TokenRole.User),
        new("/api/v1/ownDailyServices", TokenRole.User),
        new("/api/v1/geocodes", TokenRole.User),
        new("/api/v1/ips", TokenRole.User),
        new("/api/v1/passwords", TokenRole.User),
        new("/api/v1/emailAccounts", TokenRole.User),
        new("/api/v1/softwarelicenses", TokenRole.User),
        new("/api/v1/softwarelicenses/types", TokenRole.User),
        new("/api/v1/phoneCalls", TokenRole.User),
        new("/api/v1/documents", TokenRole.User),
        new("/api/v1/guarantee", TokenRole.User),
        new("/api/v1/favorites", TokenRole.User),
        new("/api/v1/sentry", TokenRole.User),
        new("/api/v1/git/commits", TokenRole.User),
        new("/api/v1/knowledgeBase", TokenRole.User),
        new("/api/v1/ical/fetch", TokenRole.None),
        new("/api/v1/ical", TokenRole.User),
        new("/api/v1/popUpNotifications", TokenRole.User),
        new("/api/v1/supportTypes", TokenRole.User),
        new("/api/v1/assignments", TokenRole.User),
        new("/api/v1/contracts", TokenRole.User),
        new("/api/v1/starface/inc", TokenRole.None, MinSegmentsBelow: 2),
        new("/api/v1/starface", TokenRole.User),
        new("/api/v1/portal", TokenRole.User),
        new("/api/v1/mention", TokenRole.User),
        new("/api/v1/customerNotifications", TokenRole.User),
        new("/api/v1/salesTarget", TokenRole.User),
        new("/api/v1/logo", TokenRole.None),
        new("/api/v1/supportRules", TokenRole.User),
        new("/api/v1/browser", TokenRole.User),
        new("/api/v1/customerPortalWizards", TokenRole.User),
        new("/api/v1/accountingTypes", TokenRole.User),
        new("/api/v1/supportProfileCategories", TokenRole.User),
        new("/api/v1/landingPage/ticketWorkflow", TokenRole.LandingPageTicketWorkflow),
        new("/api/v1/landingPage/contractWorkflow", TokenRole.LandingPageContractWorkflow),
        new("/api/v1/workflowContracts", TokenRole.User),
        new("/api/v1/ticketWorkflows", TokenRole.User),
        new("/api/v1/entityFiles", TokenRole.User),
        new("/api/v1/tempCache", TokenRole.User),
        new("/api/v1/permissions", TokenRole.User),
        new("/api/v1/ai", TokenRole.User),
        new("/api/v1/admin/ai", TokenRole.User),
        new("/api/v1/admin/permissionPackages", TokenRole.User),
    ];

    /// <summary>
    /// Die Rolle, die der Pfad verlangt; <see cref="TokenRole.Denied"/> für alles, was in
    /// keiner Zeile der Tabelle steht.
    /// </summary>
    /// <param name="path">
    /// Der Pfad unterhalb der Basisadresse, etwa <c>/api/v1/tickets/own</c> — mit oder ohne
    /// Abfragezeichenkette. Eine vollständige Adresse wird ab ihrem ersten <c>/api/</c>
    /// gelesen.
    ///</param>
    public static TokenRole RequiredFor(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string bare = Clean(path);
        foreach (Rule rule in Table)
        {
            if (rule.Matches(bare))
            {
                return rule.Role;
            }
        }

        return TokenRole.Denied;
    }

    /// <summary>
    /// Schließt diese Rolle <c>USER</c> ein — verlangt die Route also einen Mitarbeiterkontext?
    /// </summary>
    /// <remarks>
    /// <para>Wahr für <see cref="TokenRole.User"/> und die gemischten Zeilen, in denen neben einem
    /// zweiten Token auch das Login-Token zugelassen ist (<see cref="TokenRole.UserOrOffer"/>,
    /// <see cref="TokenRole.UserOrPhp"/>, <see cref="TokenRole.UserOrLandingPage"/>). Falsch für
    /// <see cref="TokenRole.None"/>: Wo gar kein Token nötig ist, gibt es auch keinen Mitarbeiter.</para>
    /// <para>Das ist die Bedingung, unter der <see cref="Rest.LoggedInUserIdHandler"/> den
    /// Abfrageparameter <c>loggedInUserId</c> anhängt: Der Server spricht einem <c>TANSS_APP</c>-Token
    /// die Rolle <c>ROLE_USER</c> nur dann zu, wenn der Parameter in der Anfrage steht.</para>
    /// </remarks>
    /// <param name="role">Die Rolle, die der Pfad verlangt.</param>
    public static bool IncludesUser(TokenRole role) =>
        role is TokenRole.User or TokenRole.UserOrOffer or TokenRole.UserOrPhp or TokenRole.UserOrLandingPage;

    /// <summary>
    /// Öffnet das Login-Token des Technikers diese Route? Wahr für die USER-Präfixe und für
    /// alles, was gar kein Token braucht.
    /// </summary>
    /// <param name="path">Der Pfad unterhalb der Basisadresse.</param>
    public static bool IsReachableWithLoginToken(string path)
    {
        TokenRole role = RequiredFor(path);
        return role is TokenRole.None || IncludesUser(role);
    }

    private static string Clean(string path)
    {
        string bare = path.Trim();
        if (Uri.TryCreate(bare, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            bare = ApiPaths.BelowBase(uri.AbsolutePath, string.Empty);
        }

        bare = ApiPaths.StripQuery(bare);
        return bare.Length > 1 ? bare.TrimEnd('/') : bare;
    }

    private sealed record Rule(string Prefix, TokenRole Role, bool Exact = false, int MinSegmentsBelow = 0)
    {
        public bool Matches(string path)
        {
            if (string.Equals(path, Prefix, StringComparison.Ordinal))
            {
                return MinSegmentsBelow == 0;
            }

            if (Exact || !path.StartsWith(Prefix + "/", StringComparison.Ordinal))
            {
                return false;
            }

            if (MinSegmentsBelow == 0)
            {
                return true;
            }

            int segments = 0;
            foreach (string segment in path[(Prefix.Length + 1)..].Split('/'))
            {
                if (segment.Length > 0)
                {
                    segments++;
                }
            }

            return segments >= MinSegmentsBelow;
        }
    }
}
