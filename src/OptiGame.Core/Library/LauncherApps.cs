namespace OptiGame.Core.Library;

/// <summary>
/// Lanceurs des magasins (barre « Lanceurs » de Mes jeux : ouvrir, fermer). Processus relevés sur la machine de dev le 2026-10-06 :
/// <list type="bullet">
/// <item>Epic : EpicGamesLauncher.exe (Launcher\Portal\Binaries\Win64) et EpicWebHelper.exe (Launcher\Engine\Binaries\Win64) ; les
/// EOSOverlayRenderer / EpicOnlineServicesUserHelper (Epic Online Services, utilisés par les JEUX) n'en font pas partie.</item>
/// <item>GOG Galaxy : GalaxyClient.exe, QtWebEngineProcess.exe, python.exe (intégrations), GOG Galaxy Notifications Renderer.exe, tous
/// dans le dossier de Galaxy ; le service GalaxyCommunication tourne hors de la session de l'utilisateur.</item>
/// </list>
/// Ni Epic ni Galaxy n'ont de commande « quitter » (fermer leur fenêtre les range dans la zone de notification) : leurs processus de
/// la session sont arrêtés, après confirmation. Steam : « steam.exe -shutdown », sa propre fermeture.
/// </summary>
public static class LauncherApps
{
    public static string Name(GameSource store) => store switch
    {
        GameSource.Steam => "Steam",
        GameSource.Epic => "Epic Games Launcher",
        GameSource.Gog => "GOG Galaxy",
        _ => store.ToString(),
    };

    /// <summary>
    /// Dossier dont TOUS les processus appartiennent au lanceur : Epic = « …\Epic Games\Launcher » (au-dessus de l'exe), GOG = dossier
    /// de GalaxyClient.exe. Null pour Steam (fermé par sa commande) ou si le chemin n'a pas la forme attendue.
    /// </summary>
    public static string? ProcessFolder(GameSource store, string exePath)
    {
        var folder = Path.GetDirectoryName(exePath);
        if (store == GameSource.Gog) return folder;
        if (store != GameSource.Epic) return null;
        for (var dir = folder; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (Path.GetFileName(dir).Equals("Launcher", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(Path.GetDirectoryName(dir) ?? "").Equals("Epic Games", StringComparison.OrdinalIgnoreCase))
            {
                return dir;
            }
        }
        return null;
    }

    /// <summary>Processus situé dans ce dossier (ou un sous-dossier).</summary>
    public static bool BelongsTo(string folder, string? processPath) =>
        processPath is not null && processPath.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
