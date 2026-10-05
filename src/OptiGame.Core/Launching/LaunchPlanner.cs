using OptiGame.Core.Profiles;

namespace OptiGame.Core.Launching;

public enum LaunchMode
{
    /// <summary>Steam si l'exe est dans une bibliothèque Steam (appid retrouvé dans les manifestes), sinon l'exe.</summary>
    Automatic,

    /// <summary>Par Steam (overlay, DRM, options de lancement Steam).</summary>
    Steam,

    /// <summary>L'exécutable surveillé lui-même.</summary>
    Executable,

    /// <summary>Un autre programme (ex. RSI Launcher pour Star Citizen), distinct de l'exe surveillé.</summary>
    Launcher,
}

/// <summary>Ce qu'il faut exécuter pour lancer un jeu. Toujours lancé SANS droits administrateur.</summary>
/// <param name="ExePath">Programme à lancer (steam.exe, l'exe du jeu ou le lanceur).</param>
/// <param name="Arguments">Arguments (ligne de commande hors programme), éventuellement vides.</param>
/// <param name="Description">Texte affiché à l'utilisateur, ex. « Steam (appid 578080) ».</param>
public sealed record LaunchPlan(string ExePath, string Arguments, string Description)
{
    /// <summary>Ligne de commande complète (programme entre guillemets + arguments).</summary>
    public string CommandLine => Arguments.Length == 0 ? $"\"{ExePath}\"" : $"\"{ExePath}\" {Arguments}";
}

public sealed class LaunchException(string message) : Exception(message);

public static class LaunchPlanner
{
    /// <param name="steamAppIdForExe">Appid Steam du jeu dont l'exe est donné, ou null (manifestes Steam).</param>
    /// <param name="steamExe">Chemin de steam.exe, ou null si Steam n'est pas installé.</param>
    public static LaunchPlan Plan(GameProfile profile, Func<string, string?> steamAppIdForExe, string? steamExe)
    {
        var arguments = profile.LaunchArguments?.Trim() ?? "";
        switch (profile.LaunchMode)
        {
            case LaunchMode.Automatic:
                var appId = profile.SteamAppId ?? steamAppIdForExe(profile.ExePath);
                return appId is not null && steamExe is not null
                    ? Steam(appId, arguments, steamExe, automatic: true)
                    : Executable(profile.ExePath, arguments, "Exécutable du jeu (automatique)");

            case LaunchMode.Steam:
                var id = profile.SteamAppId ?? steamAppIdForExe(profile.ExePath)
                    ?? throw new LaunchException("Numéro du jeu sur Steam (appid) inconnu : renseignez-le dans la fiche du jeu, onglet Propriétés, section Lancement.");
                return Steam(id, arguments, steamExe ?? throw new LaunchException("Steam n'est pas installé (SteamExe absent du registre)."), automatic: false);

            case LaunchMode.Launcher:
                if (string.IsNullOrWhiteSpace(profile.LauncherPath))
                {
                    throw new LaunchException("Aucun lanceur choisi : indiquez-le dans la section Lancement du jeu.");
                }
                return Executable(profile.LauncherPath, arguments, $"Lanceur : {Path.GetFileName(profile.LauncherPath)}");

            default:
                return Executable(profile.ExePath, arguments, "Exécutable du jeu");
        }
    }

    public static bool IsValidSteamAppId(string? appId) => appId is { Length: > 0 and <= 10 } && appId.All(char.IsAsciiDigit);

    private static LaunchPlan Steam(string appId, string arguments, string steamExe, bool automatic)
    {
        if (!IsValidSteamAppId(appId))
        {
            throw new LaunchException($"Numéro du jeu sur Steam (appid) invalide : « {appId} ».");
        }
        // Les arguments du profil suivent l'appid : Steam les transmet au jeu.
        var args = arguments.Length == 0 ? $"-applaunch {appId}" : $"-applaunch {appId} {arguments}";
        return new LaunchPlan(steamExe, args, $"Steam (appid {appId}){(automatic ? " — automatique" : "")}");
    }

    private static LaunchPlan Executable(string exePath, string arguments, string description) =>
        new(exePath, arguments, description);
}
