namespace OptiGame.Core.Updates;

/// <summary>
/// Quand installer une mise à jour sans déranger, et comment reconnaître une mise à jour qui vient d'être installée. Décisions
/// seulement : les relevés (partie en cours, fenêtre, plein écran) sont faits par l'appli.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>Première recherche après le démarrage : jamais pendant l'ouverture de session, quand le PC est le plus chargé.</summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>Mise à jour prête mais moment mal choisi : nouvel essai après ce délai.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Fenêtre d'OptiGame fermée alors qu'une mise à jour attendait : installation après ce délai, et non tout de suite (la
    /// fenêtre se ferme aussi quand on quitte OptiGame : la mise à jour ne doit pas le relancer).
    /// </summary>
    public static readonly TimeSpan AfterWindowClosedDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Raison de ne pas installer maintenant une mise à jour automatique (OptiGame se ferme quelques secondes), ou null si le
    /// moment convient : jamais pendant une partie, ni fenêtre d'OptiGame ouverte (modifications en cours), ni application en
    /// plein écran (film, jeu sans profil, présentation).
    /// </summary>
    public static string? WhyNotNow(bool inGame, bool windowShown, bool fullscreenApp) =>
        inGame ? "une partie est en cours"
        : windowShown ? "la fenêtre d'OptiGame est ouverte"
        : fullscreenApp ? "une application est en plein écran"
        : null;

    /// <summary>Version au dernier démarrage plus ancienne que celle qui démarre : une mise à jour vient d'être installée.</summary>
    public static bool JustUpdated(string? lastRunVersion, Version current) =>
        Parse(lastRunVersion) is { } last && last < current;

    /// <summary>
    /// Raison pour laquelle cette copie d'OptiGame ne se met pas à jour elle-même, ou null si elle le peut : seulement la copie
    /// installée par l'installeur (dossier de sa clé de désinstallation), et dans Program Files, où seul un administrateur peut
    /// écrire (OptiGame, élevé, y télécharge puis lance l'installeur de la mise à jour).
    /// </summary>
    /// <param name="installLocation">Dossier inscrit par l'installeur (null s'il n'y en a pas : copie de développement).</param>
    public static string? WhyNoSelfUpdate(string? installLocation, string runningFrom, string programFiles)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return "OptiGame n'a pas été installé par son installeur (copie de développement)";
        var installed = Folder(installLocation);
        var running = Folder(runningFrom);
        if (!running.Equals(installed, StringComparison.OrdinalIgnoreCase))
        {
            return $"cette copie d'OptiGame ({running}) n'est pas celle qui est installée ({installed})";
        }
        return running.StartsWith(Folder(programFiles) + '\\', StringComparison.OrdinalIgnoreCase)
            ? null
            : $"OptiGame est installé hors de Program Files ({running}), dans un dossier où d'autres programmes peuvent écrire";
    }

    private static string Folder(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');

    /// <summary>Version d'OptiGame telle qu'affichée (« 1.2.0 »), ou null si elle n'est pas exactement de la forme X.Y.Z.</summary>
    public static Version? Parse(string? text) =>
        Version.TryParse(text, out var version) && version.Build >= 0 && version.Revision < 0 ? version : null;
}
