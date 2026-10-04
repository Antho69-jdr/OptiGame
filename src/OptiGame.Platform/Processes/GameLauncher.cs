using OptiGame.Core.Launching;
using OptiGame.Core.Library;
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
    public string? SteamAppId(GameProfile profile) => GameLibraryScanner.SteamAppIdFor(profile, GameLibraryScanner.SteamApps());

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

    /// <summary>
    /// Installation d'un jeu Steam possédé : le client Steam ouvre sa propre fenêtre (choix du disque, taille). Sans droits
    /// administrateur, comme le lancement. Lève <see cref="LaunchException"/> si Steam est introuvable.
    /// </summary>
    public void InstallSteamGame(string appId)
    {
        var steam = GameLibraryScanner.SteamExe() ?? throw new LaunchException("Steam est introuvable sur ce PC.");
        UnelevatedLauncher.Launch(steam, SteamStorePage.InstallCommandLine(steam, appId));
        log.Info($"Installation demandée au client Steam : {SteamStorePage.InstallUrl(appId)}");
    }

    /// <summary>
    /// Jeu possédé d'un autre magasin, sans droits administrateur : Epic ouvre son installation (même adresse que les raccourcis
    /// d'Epic, action « install ») ; GOG, Ubisoft et EA, vus par GOG Galaxy, sont ouverts dans Galaxy, qui propose de les installer
    /// (par Ubisoft Connect ou l'EA app pour leurs jeux). Renvoie le message à afficher.
    /// </summary>
    public string InstallStoreGame(GameSource store, string key, string name)
    {
        switch (store)
        {
            case GameSource.Epic:
                var epic = StoreLibraries.ProtocolExe("com.epicgames.launcher") ?? throw new LaunchException("Epic Games Launcher est introuvable sur ce PC.");
                var parts = key.Split(':');
                if (parts.Length != 3) throw new LaunchException($"Jeu Epic invalide : « {key} ».");
                var install = StoreLaunchers.EpicUri(parts[0], parts[1], parts[2], "install");
                UnelevatedLauncher.Launch(epic, $"\"{epic}\" {StoreLaunchers.Quoted(install)}");
                log.Info($"Installation demandée à Epic Games Launcher : {install}");
                return $"Epic Games Launcher ouvre l'installation de « {name} ».";

            case GameSource.Gog or GameSource.Ubisoft or GameSource.Ea:
                var galaxy = StoreLibraries.ProtocolExe("goggalaxy") ?? throw new LaunchException("GOG Galaxy est introuvable sur ce PC.");
                var view = StoreLaunchers.GalaxyGameViewUri(key);
                UnelevatedLauncher.Launch(galaxy, $"\"{galaxy}\" /urlProtocol=\"{view}\"");
                log.Info($"Jeu ouvert dans GOG Galaxy : {view}");
                return $"GOG Galaxy ouvre « {name} » : lancez l'installation depuis sa page.";

            default:
                throw new LaunchException($"Installation non prise en charge pour ce magasin ({store}).");
        }
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
