using System.Diagnostics;
using System.Management;
using Microsoft.Win32.SafeHandles;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Processes;

/// <summary>
/// Détecte le lancement et la fermeture des jeux sans polling :
/// - démarrages : événement WMI Win32_ProcessStartTrace (poussé par le noyau, nécessite les droits admin) ;
/// - fermetures : événement WMI Win32_ProcessStopTrace, doublé d'une attente sur un handle SYNCHRONIZE du jeu.
///   Aucun des deux ne nécessite d'autre accès au processus : les jeux protégés par un anti-cheat
///   refusent souvent l'ouverture d'un handle complet (Process.EnableRaisingEvents).
/// </summary>
public sealed class GameMonitor(GameSessionManager sessions, ProfileStore profiles, FileLog log) : IDisposable
{
    private readonly Lock _lock = new();
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private RegisteredWaitHandle? _exitWait;
    private SafeWaitHandle? _exitHandle;
    private int _startEventCount;

    /// <summary>Erreur non bloquante (ex. détection indisponible), à afficher à l'utilisateur.</summary>
    public event EventHandler<string>? Error;

    /// <summary>Reprend la surveillance d'un jeu déjà en cours (session récupérée après un crash).</summary>
    public void WatchSessionProcess(int processId) => Watch(processId);

    public void Start()
    {
        if (_startWatcher is not null) return;

        try
        {
            _startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += OnStartEvent;
            _startWatcher.Start();

            _stopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += OnStopEvent;
            _stopWatcher.Start();
        }
        catch (Exception ex)
        {
            log.Error("Détection des jeux : abonnement WMI impossible", ex);
            Dispose();
            throw;
        }

        log.Info("Détection des jeux démarrée (Win32_ProcessStartTrace / Win32_ProcessStopTrace).");

        // Jeux déjà lancés avant OptiGame, ou avant la création/activation de leur profil.
        Task.Run(ScanRunningGames);
        profiles.Changed += OnProfilesChanged;
    }

    public void Dispose()
    {
        profiles.Changed -= OnProfilesChanged;
        foreach (var watcher in new[] { _startWatcher, _stopWatcher })
        {
            try
            {
                watcher?.Stop();
            }
            catch (ManagementException)
            {
            }
            watcher?.Dispose();
        }
        _startWatcher = _stopWatcher = null;
        lock (_lock)
        {
            ReleaseExitWait();
        }
    }

    private void OnProfilesChanged(object? sender, EventArgs e) => Task.Run(ScanRunningGames);

    private void OnStartEvent(object sender, EventArrivedEventArgs e)
    {
        int pid;
        string name;
        using (e.NewEvent)
        {
            pid = (int)(uint)e.NewEvent["ProcessID"];
            name = e.NewEvent["ProcessName"] as string ?? "";
        }

        if (Interlocked.Increment(ref _startEventCount) == 1)
        {
            log.Info($"Premier événement de démarrage reçu : {name} (pid {pid}) — la détection WMI fonctionne.");
        }

        // Hors du thread WMI : l'application d'un profil peut prendre quelques secondes (fermeture de programmes).
        Task.Run(() => Handle(pid, name));
    }

    private void OnStopEvent(object sender, EventArrivedEventArgs e)
    {
        int pid;
        using (e.NewEvent)
        {
            pid = (int)(uint)e.NewEvent["ProcessID"];
        }

        if (sessions.Current?.ProcessId == pid)
        {
            Task.Run(() => OnExited(pid, "événement WMI"));
        }
    }

    private void ScanRunningGames()
    {
        foreach (var profile in profiles.GetAll().Where(p => p.Enabled))
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(profile.ExePath)))
            {
                using (process)
                {
                    Handle(process.Id, Path.GetFileName(profile.ExePath));
                }
            }
        }
    }

    private void Handle(int pid, string eventName)
    {
        try
        {
            // Comparaison sur le chemin complet ; le nom de l'événement peut être tronqué.
            var path = ProcessInfo.GetPath(pid);
            var candidate = profiles.GetAll().FirstOrDefault(p =>
                Path.GetFileName(p.ExePath).Equals(eventName, StringComparison.OrdinalIgnoreCase) ||
                (path is not null && p.Matches(path)));
            if (candidate is null)
            {
                return; // Processus sans rapport avec un profil : rien à journaliser.
            }

            if (path is null)
            {
                log.Warn($"{eventName} (pid {pid}) a démarré mais son chemin est illisible (processus déjà terminé ou accès refusé).");
                return;
            }

            if (profiles.FindEnabledFor(path) is not { } profile)
            {
                log.Info(candidate.Enabled
                    ? $"{eventName} (pid {pid}) : chemin {path} différent de celui du profil « {candidate.Name} » ({candidate.ExePath})."
                    : $"{eventName} (pid {pid}) : le profil « {candidate.Name} » est désactivé.");
                return;
            }

            if (sessions.OnProcessStarted(pid, path) is null)
            {
                log.Info($"{eventName} (pid {pid}) ignoré : une session est déjà en cours ({sessions.Current?.Profile.Name}).");
                return;
            }

            log.Info($"Session démarrée pour « {profile.Name} » (pid {pid}).");
            Watch(pid);
        }
        catch (Exception ex)
        {
            log.Error($"Erreur lors du traitement de {eventName} (pid {pid})", ex);
            Error?.Invoke(this, ex.Message);
        }
    }

    /// <summary>Attente de fin via un handle SYNCHRONIZE (le droit minimal, accordé même aux processus protégés).</summary>
    private void Watch(int pid)
    {
        lock (_lock)
        {
            ReleaseExitWait();
            var raw = OpenProcess(Synchronize, false, pid);
            if (raw == IntPtr.Zero)
            {
                log.Warn($"Handle SYNCHRONIZE refusé pour le pid {pid} : la fin de partie sera détectée par l'événement WMI uniquement.");
                return;
            }

            _exitHandle = new SafeWaitHandle(raw, ownsHandle: true);
            var waitHandle = new ManualResetEvent(false) { SafeWaitHandle = _exitHandle };
            _exitWait = ThreadPool.RegisterWaitForSingleObject(waitHandle,
                (_, _) => OnExited(pid, "handle du processus"), null, Timeout.Infinite, executeOnlyOnce: true);
        }
    }

    private void ReleaseExitWait()
    {
        _exitWait?.Unregister(null);
        _exitWait = null;
        _exitHandle?.Dispose();
        _exitHandle = null;
    }

    private void OnExited(int pid, string source)
    {
        try
        {
            var (outcome, newPid) = sessions.OnProcessExited(pid);
            switch (outcome)
            {
                case ExitOutcome.Ended:
                    log.Info($"Fin de partie détectée (pid {pid}, {source}) : réglages restaurés.");
                    lock (_lock) ReleaseExitWait();
                    break;
                case ExitOutcome.Continued when newPid is { } next:
                    log.Info($"Le pid {pid} s'est terminé mais une autre instance du jeu tourne (pid {next}) : la session continue.");
                    Watch(next);
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Error($"Erreur à la fin de partie (pid {pid})", ex);
            Error?.Invoke(this, ex.Message);
        }
    }
}
