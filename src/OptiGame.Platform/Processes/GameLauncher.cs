using System.Diagnostics;
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
    /// Page d'un magasin (Epic Games, GOG) dans le navigateur de la session, sans droits administrateur. Seules les adresses https de
    /// ces magasins sont ouvertes (jamais une adresse reçue d'ailleurs).
    /// </summary>
    public void OpenStoreWebPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host is not ("store.epicgames.com" or "www.gog.com"))
        {
            throw new ArgumentException($"Adresse de magasin refusée : {url}", nameof(url));
        }
        // explorer.exe transmet l'adresse au navigateur de la session, sans droits administrateur.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", uri.AbsoluteUri) { UseShellExecute = true });
        log.Info($"Page du magasin ouverte dans le navigateur : {uri.AbsoluteUri}");
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
    /// d'Epic, action « install ») ; GOG ouvre la page du jeu dans GOG Galaxy, qui propose de l'installer. Renvoie le message à afficher.
    /// </summary>
    public string InstallStoreGame(GameSource store, string key, string name)
    {
        switch (store)
        {
            case GameSource.Epic:
                var epic = StoreLibraries.ProtocolExe("com.epicgames.launcher") ?? throw new LaunchException("Epic Games Launcher est introuvable sur ce PC.");
                var parts = key.Split(':');
                if (parts.Length != 3) throw new LaunchException($"Jeu Epic invalide : « {key} ».");
                var install = StoreLaunchers.EpicUri(parts[0], parts[1], parts[2], "install", silent: false);
                var wasRunning = Process.GetProcessesByName("EpicGamesLauncher").Length > 0;
                UnelevatedLauncher.Launch(epic, $"\"{epic}\" {StoreLaunchers.Quoted(install)}");
                log.Info($"Installation demandée à Epic Games Launcher{(wasRunning ? "" : " (fermé : il démarre)")} : {install}");
                if (wasRunning) return $"Epic Games Launcher ouvre l'installation de « {name} ».";
                // Lanceur fermé : il démarre, mais ignore la demande reçue au démarrage. Elle est renvoyée quand sa fenêtre est prête.
                _ = ResendWhenEpicIsReadyAsync(epic, install);
                return $"Epic Games Launcher démarre : l'installation de « {name} » s'ouvrira dès qu'il sera prêt (quelques secondes).";

            case GameSource.Gog:
                var galaxy = StoreLibraries.ProtocolExe("goggalaxy") ?? throw new LaunchException("GOG Galaxy est introuvable sur ce PC.");
                var view = StoreLaunchers.GalaxyGameViewUri(key);
                UnelevatedLauncher.Launch(galaxy, $"\"{galaxy}\" /urlProtocol=\"{view}\"");
                log.Info($"Jeu ouvert dans GOG Galaxy : {view}");
                return $"GOG Galaxy ouvre « {name} » : lancez l'installation depuis sa page.";

            default:
                throw new LaunchException($"Installation non prise en charge pour ce magasin ({store}).");
        }
    }

    /// <summary>
    /// Epic Games Launcher démarré par une demande d'installation : on attend sa fenêtre (60 s au plus, il se connecte et se met
    /// parfois à jour), puis quelques secondes, et la demande est renvoyée une fois à l'instance prête.
    /// </summary>
    private async Task ResendWhenEpicIsReadyAsync(string epic, string install)
    {
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && !Process.GetProcessesByName("EpicGamesLauncher").Any(HasWindow))
            {
                await Task.Delay(1000);
            }
            await Task.Delay(TimeSpan.FromSeconds(4));
            UnelevatedLauncher.Launch(epic, $"\"{epic}\" {StoreLaunchers.Quoted(install)}");
            log.Info($"Installation renvoyée à Epic Games Launcher, maintenant prêt : {install}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or LaunchException)
        {
            log.Warn($"Installation non renvoyée à Epic Games Launcher : {ex.Message}");
        }

        static bool HasWindow(Process process)
        {
            using (process)
            {
                try
                {
                    return process.MainWindowHandle != IntPtr.Zero;
                }
                catch (InvalidOperationException)
                {
                    return false; // processus terminé entre-temps
                }
            }
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
