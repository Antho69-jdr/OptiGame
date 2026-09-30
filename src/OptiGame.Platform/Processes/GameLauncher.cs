using OptiGame.Core.Launching;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Platform.Library;

namespace OptiGame.Platform.Processes;

/// <summary>
/// Lance un jeu depuis OptiGame, toujours SANS droits administrateur (jeton de l'Explorateur) : un jeu lancé en admin
/// pose problème (anti-cheat, overlays, sauvegardes). Le profil s'applique ensuite par la détection habituelle.
/// </summary>
public sealed class GameLauncher(FileLog log)
{
    /// <summary>Ce qui sera lancé (aperçu dans la page du jeu). Lit les manifestes Steam : à appeler hors du thread UI.</summary>
    public LaunchPlan Plan(GameProfile profile)
    {
        var apps = new Lazy<IReadOnlyList<GameLibraryScanner.SteamApp>>(GameLibraryScanner.SteamApps);
        return LaunchPlanner.Plan(profile, exe => GameLibraryScanner.FindSteamAppId(exe, apps.Value), GameLibraryScanner.SteamExe());
    }

    /// <summary>Appid Steam du jeu (profil, sinon manifestes Steam), ou null si ce n'est pas un jeu Steam. Hors du thread UI.</summary>
    public string? SteamAppId(GameProfile profile) =>
        profile.SteamAppId is { } id && LaunchPlanner.IsValidSteamAppId(id)
            ? id
            : GameLibraryScanner.FindSteamAppId(profile.ExePath, GameLibraryScanner.SteamApps());

    /// <summary>Page du jeu dans le magasin : client Steam (sans droits administrateur), sinon navigateur.</summary>
    public void OpenStorePage(string appId)
    {
        if (GameLibraryScanner.SteamExe() is { } steam)
        {
            UnelevatedLauncher.Launch(steam, SteamStorePage.ClientCommandLine(steam, appId));
            log.Info($"Page Steam ouverte dans le client : {SteamStorePage.ClientUrl(appId)}");
            return;
        }
        // explorer.exe transmet l'adresse au navigateur de la session, sans droits administrateur.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", SteamStorePage.WebUrl(appId)) { UseShellExecute = true });
        log.Info($"Steam absent : page ouverte dans le navigateur ({SteamStorePage.WebUrl(appId)})");
    }

    public LaunchPlan Launch(GameProfile profile)
    {
        var plan = Plan(profile);
        if (!File.Exists(plan.ExePath))
        {
            throw new LaunchException($"Programme introuvable : {plan.ExePath}");
        }

        UnelevatedLauncher.Launch(plan.ExePath, plan.CommandLine);
        log.Info($"Lancement de « {profile.Name} » : {plan.Description} → {plan.CommandLine}");
        return plan;
    }
}
