namespace OptiGame.Core.Launching;

/// <summary>
/// Page d'un jeu dans le magasin Steam : dans le client Steam (déjà connecté : liste de souhaits, avis…) ou, sans
/// Steam, dans le navigateur. L'appid est toujours vérifié (chiffres uniquement) avant d'entrer dans une commande.
/// </summary>
public static class SteamStorePage
{
    public static string ClientUrl(string appId) => $"steam://store/{Checked(appId)}";

    public static string WebUrl(string appId) => $"https://store.steampowered.com/app/{Checked(appId)}/";

    /// <summary>
    /// Ligne de commande de steam.exe, sous la forme que Windows enregistre pour le protocole steam://
    /// (HKCR\steam\shell\open\command : <c>"steam.exe" -- "%1"</c>, vérifié sur la machine de dev).
    /// </summary>
    public static string ClientCommandLine(string steamExe, string appId) => $"\"{steamExe}\" -- \"{ClientUrl(appId)}\"";

    /// <summary>Installation d'un jeu possédé : le client Steam ouvre sa fenêtre d'installation (choix du disque), rien de plus.</summary>
    public static string InstallUrl(string appId) => $"steam://install/{Checked(appId)}";

    public static string InstallCommandLine(string steamExe, string appId) => $"\"{steamExe}\" -- \"{InstallUrl(appId)}\"";

    private static string Checked(string appId) =>
        LaunchPlanner.IsValidSteamAppId(appId) ? appId : throw new ArgumentException($"Appid Steam invalide : « {appId} ».", nameof(appId));
}
