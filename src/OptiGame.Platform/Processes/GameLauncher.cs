using System.Diagnostics;
using OptiGame.Core.Launching;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Core.Updates;
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
        OpenInBrowser(SteamStorePage.WebUrl(appId));
        log.Info($"Steam absent : page ouverte dans le navigateur ({SteamStorePage.WebUrl(appId)})");
    }

    /// <summary>
    /// Adresse ouverte par le navigateur par défaut (commande enregistrée pour https, BrowserCommand), lancé SANS droits
    /// administrateur. explorer.exe seulement à défaut : il ouvre « Documents » pour une adresse avec « ? » et « &amp; ».
    /// </summary>
    private static void OpenInBrowser(string url)
    {
        string? registered = null;
        using (var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
        {
            if (choice?.GetValue("ProgId") is string progId && progId.Length > 0)
            {
                using var command = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
                registered = command?.GetValue(null) as string;
            }
        }
        if (BrowserCommand.Build(registered, url) is { } browser && File.Exists(browser.Exe))
        {
            UnelevatedLauncher.Launch(browser.Exe, browser.CommandLine);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", url) { UseShellExecute = true });
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
        OpenInBrowser(uri.AbsoluteUri);
        log.Info($"Page du magasin ouverte dans le navigateur : {uri.AbsoluteUri}");
    }

    /// <summary>Critiques de la presse citées par Steam (adresse metacritic.com de appdetails), dans le navigateur par défaut.</summary>
    public void OpenPressPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "www.metacritic.com")
        {
            throw new ArgumentException($"Adresse de la presse refusée : {url}", nameof(url));
        }
        OpenInBrowser(uri.AbsoluteUri);
        log.Info($"Critiques de la presse ouvertes dans le navigateur : {uri.AbsoluteUri}");
    }

    /// <summary>
    /// Bande-annonce hors Steam (GOG, IGDB), dans le navigateur par défaut : seules les adresses produites par
    /// <see cref="Core.Library.WebVideo"/> (page YouTube d'une vidéo, lecteur fast.wistia.net de GOG).
    /// </summary>
    public void OpenWebVideo(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(Core.Library.WebVideo.Normalize(url) == url
                 || (uri.Host == "www.youtube.com" && uri.AbsolutePath == "/watch"
                     && Core.Library.WebVideo.YouTube(System.Web.HttpUtility.ParseQueryString(uri.Query)["v"]) == url)))
        {
            throw new ArgumentException($"Adresse de vidéo refusée : {url}", nameof(url));
        }
        OpenInBrowser(uri.AbsoluteUri);
        log.Info($"Bande-annonce ouverte dans le navigateur : {uri.AbsoluteUri}");
    }

    /// <summary>
    /// Connexion avec Steam des appels : page du serveur de mise en relation (https *.workers.dev, /v1/auth/steam), qui mène à la page
    /// de connexion de Steam, dans le navigateur par défaut, sans droits administrateur.
    /// </summary>
    public void OpenSteamLogin(Uri page)
    {
        if (page.Scheme != Uri.UriSchemeHttps || !page.Host.EndsWith(".workers.dev", StringComparison.OrdinalIgnoreCase)
            || page.AbsolutePath != "/v1/auth/steam")
        {
            throw new ArgumentException($"Adresse de connexion refusée : {page}", nameof(page));
        }
        OpenInBrowser(page.AbsoluteUri);
        log.Info("Connexion avec Steam ouverte dans le navigateur.");
    }

    /// <summary>Page du projet OptiGame sur GitHub (politique de confidentialité…), dans le navigateur par défaut.</summary>
    public void OpenProjectPage(Uri page)
    {
        if (page.Scheme != Uri.UriSchemeHttps || page.Host != "github.com"
            || !page.AbsolutePath.StartsWith($"/{AppReleases.Owner}/{AppReleases.Repository}/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Adresse refusée : {page}", nameof(page));
        }
        OpenInBrowser(page.AbsoluteUri);
        log.Info($"Page du projet ouverte dans le navigateur : {page.AbsoluteUri}");
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
                var install = StoreLaunchers.EpicUri(parts[0], parts[1], parts[2], "install");
                return SendToEpic(epic, install)
                    ? $"Epic Games Launcher ouvre l'installation de « {name} »."
                    : $"Epic Games Launcher démarre : l'installation de « {name} » s'ouvrira dès qu'il sera prêt (quelques secondes).";

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
    /// « Voir sur … » DANS le lanceur : Epic = recherche du jeu dans sa boutique (ses pages produit y sont introuvables), GOG =
    /// page du jeu dans la boutique de Galaxy. Faux si le lanceur est absent ou l'adresse inutilisable : l'appelant ouvre alors le
    /// navigateur.
    /// </summary>
    public bool OpenStorePageInLauncher(StoreProduct product, string webUrl)
    {
        switch (product.Store)
        {
            case GameSource.Epic when StoreLibraries.ProtocolExe("com.epicgames.launcher") is { } epic:
                SendToEpic(epic, StorePages.EpicLauncherSearchUri(product.Title));
                return true;
            case GameSource.Gog when StoreLibraries.ProtocolExe("goggalaxy") is { } galaxy && StorePages.GalaxyStoreUri(webUrl) is { } page:
                UnelevatedLauncher.Launch(galaxy, $"\"{galaxy}\" /urlProtocol=\"{page}\"");
                log.Info($"Page du jeu ouverte dans GOG Galaxy : {page}");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Adresse transmise au lanceur Epic. Fermé, il démarre AVEC une adresse en restant caché et la perd (constaté le 2026-10-06 :
    /// aucune fenêtre en 60 s) ; démarré seul, sa fenêtre s'ouvre en ≈ 6 s : il est démarré seul, l'adresse suit quand il est prêt.
    /// Vrai si le lanceur tournait déjà (l'adresse est traitée tout de suite).
    /// </summary>
    private bool SendToEpic(string epic, string uri)
    {
        if (Process.GetProcessesByName("EpicGamesLauncher").Length > 0)
        {
            UnelevatedLauncher.Launch(epic, $"\"{epic}\" {StoreLaunchers.Quoted(uri)}");
            log.Info($"Adresse transmise à Epic Games Launcher : {uri}");
            return true;
        }
        UnelevatedLauncher.Launch(epic, $"\"{epic}\"");
        log.Info($"Epic Games Launcher fermé : démarré, l'adresse suivra ({uri}).");
        _ = SendWhenEpicIsReadyAsync(epic, uri);
        return false;
    }

    /// <summary>
    /// Epic Games Launcher démarré seul : on attend sa fenêtre (60 s au plus, il se connecte et se met
    /// parfois à jour), puis quelques secondes, et l'adresse (installation, boutique) est envoyée à l'instance prête.
    /// </summary>
    private async Task SendWhenEpicIsReadyAsync(string epic, string uri)
    {
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && !Process.GetProcessesByName("EpicGamesLauncher").Any(HasWindow))
            {
                await Task.Delay(1000);
            }
            await Task.Delay(TimeSpan.FromSeconds(8)); // fenêtre affichée avant la fin du chargement de la boutique
            UnelevatedLauncher.Launch(epic, $"\"{epic}\" {StoreLaunchers.Quoted(uri)}");
            log.Info($"Adresse transmise à Epic Games Launcher, maintenant prêt : {uri}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or LaunchException)
        {
            log.Warn($"Adresse non transmise à Epic Games Launcher : {ex.Message}");
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
