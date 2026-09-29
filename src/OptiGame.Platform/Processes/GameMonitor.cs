using System.Diagnostics;
using System.Management;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;

namespace OptiGame.Platform.Processes;

/// <summary>
/// Détecte le lancement et la fermeture des jeux sans polling :
/// - démarrages : événement WMI Win32_ProcessStartTrace (poussé par le noyau, nécessite les droits admin) ;
/// - fermeture : attente sur le handle du processus du jeu (Process.Exited).
/// </summary>
public sealed class GameMonitor(GameSessionManager sessions, ProfileStore profiles) : IDisposable
{
    private readonly Lock _lock = new();
    private ManagementEventWatcher? _watcher;
    private Process? _watched;

    /// <summary>Erreur non bloquante (ex. détection indisponible), à afficher à l'utilisateur.</summary>
    public event EventHandler<string>? Error;

    public bool IsRunning => _watcher is not null;

    /// <summary>Reprend la surveillance d'un jeu déjà en cours (session récupérée après un crash).</summary>
    public void WatchSessionProcess(int processId) => Watch(processId);

    public void Start()
    {
        if (_watcher is not null) return;

        var watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStartTrace"));
        watcher.EventArrived += OnProcessStarted;
        watcher.Start();
        _watcher = watcher;

        // Jeux déjà lancés avant OptiGame, ou avant la création/activation de leur profil.
        Task.Run(ScanRunningGames);
        profiles.Changed += OnProfilesChanged;
    }

    private void OnProfilesChanged(object? sender, EventArgs e) => Task.Run(ScanRunningGames);

    public void Dispose()
    {
        profiles.Changed -= OnProfilesChanged;
        _watcher?.Stop();
        _watcher?.Dispose();
        _watcher = null;
        lock (_lock)
        {
            _watched?.Dispose();
            _watched = null;
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        int pid;
        using (e.NewEvent)
        {
            pid = (int)(uint)e.NewEvent["ProcessID"];
        }
        // Hors du thread WMI : l'application d'un profil peut prendre quelques secondes (fermeture de programmes).
        Task.Run(() => Handle(pid));
    }

    private void ScanRunningGames()
    {
        foreach (var profile in profiles.GetAll().Where(p => p.Enabled))
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(profile.ExePath)))
            {
                using (process)
                {
                    Handle(process.Id);
                }
            }
        }
    }

    private void Handle(int pid)
    {
        try
        {
            // Comparaison sur le chemin complet (ProcessName de l'événement peut être tronqué).
            var path = ProcessInfo.GetPath(pid);
            if (path is null || profiles.FindEnabledFor(path) is null) return;

            if (sessions.OnProcessStarted(pid, path) is not null)
            {
                Watch(pid);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex.Message);
        }
    }

    private void Watch(int pid)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
            process.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Déjà terminé avant qu'on s'y attache.
            Task.Run(() => OnExited(pid));
            return;
        }

        lock (_lock)
        {
            _watched?.Dispose();
            _watched = process;
        }
        process.Exited += (_, _) => Task.Run(() => OnExited(pid));
        if (process.HasExited)
        {
            Task.Run(() => OnExited(pid));
        }
    }

    private void OnExited(int pid)
    {
        try
        {
            var (outcome, newPid) = sessions.OnProcessExited(pid);
            if (outcome == ExitOutcome.Continued && newPid is { } next)
            {
                Watch(next);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex.Message);
        }
    }
}
