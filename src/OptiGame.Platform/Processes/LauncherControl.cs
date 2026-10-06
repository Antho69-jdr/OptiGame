using System.ComponentModel;
using System.Diagnostics;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Platform.Library;

namespace OptiGame.Platform.Processes;

/// <summary>Lanceur installé sur ce PC.</summary>
public sealed record LauncherApp(GameSource Store, string Name, string ExePath);

/// <summary>
/// Barre « Lanceurs » de Mes jeux : quels lanceurs sont installés et ouverts, les ouvrir (sans droits administrateur), les fermer.
/// Steam se ferme par sa propre commande (« -shutdown ») ; Epic et GOG Galaxy n'en ont pas (fermer leur fenêtre les range dans la
/// zone de notification) : leurs processus de la session, dans leur dossier (Core/Library/LauncherApps), sont arrêtés — jamais un
/// service, un composant de Windows, Epic Online Services (utilisé par les jeux) ni OptiGame.
/// </summary>
public sealed class LauncherControl(FileLog log)
{
    public IReadOnlyList<LauncherApp> Installed()
    {
        var launchers = new List<LauncherApp>();
        if (GameLibraryScanner.SteamExe() is { } steam && File.Exists(steam)) launchers.Add(new(GameSource.Steam, LauncherApps.Name(GameSource.Steam), steam));
        if (StoreLibraries.ProtocolExe("com.epicgames.launcher") is { } epic) launchers.Add(new(GameSource.Epic, LauncherApps.Name(GameSource.Epic), epic));
        if (StoreLibraries.ProtocolExe("goggalaxy") is { } galaxy) launchers.Add(new(GameSource.Gog, LauncherApps.Name(GameSource.Gog), galaxy));
        return launchers;
    }

    public bool IsRunning(LauncherApp launcher)
    {
        var processes = ProcessInfo.InSession(Path.GetFileName(launcher.ExePath));
        processes.ForEach(p => p.Dispose());
        return processes.Count > 0;
    }

    /// <summary>Ouvre le lanceur, ou l'affiche s'il tourne déjà (une seconde instance passe la main à la première).</summary>
    public void Open(LauncherApp launcher)
    {
        UnelevatedLauncher.Launch(launcher.ExePath, $"\"{launcher.ExePath}\"");
        log.Info($"{launcher.Name} ouvert depuis Mes jeux.");
    }

    /// <summary>Ferme le lanceur ; lève InvalidOperationException s'il tourne encore après l'attente.</summary>
    public async Task CloseAsync(LauncherApp launcher)
    {
        if (launcher.Store == GameSource.Steam)
        {
            UnelevatedLauncher.Launch(launcher.ExePath, $"\"{launcher.ExePath}\" -shutdown");
            log.Info("Fermeture demandée à Steam (-shutdown).");
            if (!await WaitClosedAsync(launcher, TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException("Steam ne s'est pas fermé : une partie, un téléchargement ou une fenêtre de Steam le retient peut-être.");
            }
            return;
        }

        var folder = LauncherApps.ProcessFolder(launcher.Store, launcher.ExePath)
            ?? throw new InvalidOperationException($"Dossier de {launcher.Name} inattendu : {launcher.ExePath}");
        var stopped = await Task.Run(() => StopProcessesIn(folder));
        log.Info($"{launcher.Name} fermé depuis Mes jeux ({stopped} processus arrêtés dans {folder}).");
        if (!await WaitClosedAsync(launcher, TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException($"{launcher.Name} n'a pas pu être fermé.");
        }
    }

    private int StopProcessesIn(string folder)
    {
        var stopped = 0;
        var session = ProcessInfo.CurrentSessionId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.SessionId != session || process.Id == ProcessInfo.CurrentProcessId) continue;
                var path = ProcessInfo.GetPath(process.Id);
                if (!LauncherApps.BelongsTo(folder, path) || ProcessInfo.IsWindowsComponent(path)) continue;
                try
                {
                    process.Kill();
                    process.WaitForExit(TimeSpan.FromSeconds(3));
                    stopped++;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Déjà terminé (un processus enfant s'arrête avec son parent) ou accès refusé : vérifié ensuite.
                }
            }
        }
        return stopped;
    }

    private async Task<bool> WaitClosedAsync(LauncherApp launcher, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning(launcher)) return true;
            await Task.Delay(500);
        }
        return !IsRunning(launcher);
    }
}
