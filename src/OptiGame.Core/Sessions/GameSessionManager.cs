using OptiGame.Core.Abstractions;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Sessions;

/// <summary>Actions sur les processus qui ne sont pas des réglages journalisables.</summary>
public interface IProcessControl
{
    /// <summary>Ferme toutes les instances de l'exe dans la session (normalement, puis de force). Renvoie le nombre fermé.</summary>
    int Close(string exeName);

    /// <summary>Renvoie null si la priorité a été appliquée, sinon la raison de l'échec.</summary>
    string? TrySetPriority(int processId, GamePriority priority);

    /// <summary>Processus en cours dont l'exécutable est exactement ce chemin.</summary>
    IReadOnlyList<int> FindProcesses(string exePath);
}

public sealed record ActiveSession(GameProfile Profile, int ProcessId, DateTimeOffset StartedAt);

public sealed record SessionStartReport(GameProfile Profile, IReadOnlyList<string> Applied, IReadOnlyList<string> Warnings);

public sealed record SessionEndReport(string GameName, RestoreReport Restore, IReadOnlyList<string> Warnings, bool AfterCrash)
{
    public bool Success => Restore.Success && Warnings.Count == 0;
}

public enum ExitOutcome
{
    /// <summary>Le processus ne correspond pas à la session en cours.</summary>
    Ignored,

    /// <summary>Une autre instance du jeu tourne encore : la session continue avec elle.</summary>
    Continued,

    Ended,
}

public enum RecoveryOutcome
{
    Nothing,

    /// <summary>Session interrompue (crash d'OptiGame) et jeu fermé : réglages restaurés.</summary>
    Restored,

    /// <summary>Session interrompue mais le jeu tourne encore : la session reprend.</summary>
    Resumed,
}

/// <summary>
/// Applique le profil d'un jeu à son lancement et restaure tout à sa fermeture, via le journal de session
/// (write-ahead) : un crash d'OptiGame en pleine partie est rattrapé au démarrage suivant.
/// Une seule session à la fois.
/// </summary>
public sealed class GameSessionManager(
    ChangeJournal journal,
    ProfileStore profiles,
    SettingAccessors settings,
    IPowerSchemeProvider power,
    IProcessControl processes,
    TimeProvider time)
{
    private readonly Lock _lock = new();

    public event EventHandler<SessionStartReport>? SessionStarted;

    public event EventHandler<SessionEndReport>? SessionEnded;

    public ActiveSession? Current
    {
        get { lock (_lock) return _current; }
    }

    private ActiveSession? _current;

    /// <summary>À appeler au démarrage d'OptiGame, avant la détection des jeux.</summary>
    public (RecoveryOutcome Outcome, SessionEndReport? Report, int? ProcessId) Recover()
    {
        SessionEndReport? report;
        lock (_lock)
        {
            if (!journal.HasPendingEntries)
            {
                return (RecoveryOutcome.Nothing, null, null);
            }

            var exePath = journal.Context;
            var running = exePath is null ? [] : processes.FindProcesses(exePath);
            if (running.Count > 0)
            {
                var profile = profiles.GetAll().FirstOrDefault(p => p.Matches(exePath!))
                    ?? new GameProfile { Name = Path.GetFileNameWithoutExtension(exePath!), ExePath = exePath! };
                _current = new ActiveSession(profile, running[0], time.GetUtcNow());
                return (RecoveryOutcome.Resumed, null, running[0]);
            }

            report = RestoreLocked(GameName(exePath), afterCrash: true);
        }

        SessionEnded?.Invoke(this, report);
        return (RecoveryOutcome.Restored, report, null);
    }

    /// <summary>Un processus a démarré. Renvoie un rapport si une session a commencé.</summary>
    public SessionStartReport? OnProcessStarted(int processId, string exePath)
    {
        SessionStartReport report;
        lock (_lock)
        {
            if (_current is not null)
            {
                return null;
            }

            var profile = profiles.FindEnabledFor(exePath);
            if (profile is null)
            {
                return null;
            }

            _current = new ActiveSession(profile, processId, time.GetUtcNow());
            journal.SetContext(profile.ExePath);
            report = ApplyProfile(profile, processId);
        }

        SessionStarted?.Invoke(this, report);
        return report;
    }

    /// <summary>Un processus s'est terminé. Si c'était le jeu de la session, restaure tout.</summary>
    public (ExitOutcome Outcome, int? NewProcessId) OnProcessExited(int processId)
    {
        SessionEndReport report;
        lock (_lock)
        {
            if (_current is null || _current.ProcessId != processId)
            {
                return (ExitOutcome.Ignored, null);
            }

            var others = processes.FindProcesses(_current.Profile.ExePath).Where(pid => pid != processId).ToList();
            if (others.Count > 0)
            {
                _current = _current with { ProcessId = others[0] };
                return (ExitOutcome.Continued, others[0]);
            }

            report = RestoreLocked(_current.Profile.Name, afterCrash: false);
        }

        SessionEnded?.Invoke(this, report);
        return (ExitOutcome.Ended, null);
    }

    /// <summary>Termine la session tout de suite (action de l'utilisateur) et restaure tout.</summary>
    public SessionEndReport? EndNow()
    {
        SessionEndReport report;
        lock (_lock)
        {
            if (_current is null && !journal.HasPendingEntries)
            {
                return null;
            }
            report = RestoreLocked(_current?.Profile.Name ?? GameName(journal.Context), afterCrash: false);
        }

        SessionEnded?.Invoke(this, report);
        return report;
    }

    private SessionStartReport ApplyProfile(GameProfile profile, int processId)
    {
        var applied = new List<string>();
        var warnings = new List<string>();

        IReadOnlyList<PowerScheme> schemes;
        try
        {
            schemes = power.GetSchemes();
        }
        catch (Exception)
        {
            schemes = [];
        }

        if (SessionPlan.PowerChange(profile, schemes) is { } powerChange)
        {
            Try(() => journal.Apply(powerChange), powerChange.What, $"Plan d'alimentation non modifié", applied, warnings);
        }

        foreach (var process in profile.ProcessesToClose)
        {
            if (process.Relaunch)
            {
                // Journalisé uniquement s'il tourne : sinon la restauration fermerait un programme ouvert pendant la partie.
                SettingValue current;
                try
                {
                    current = settings.Read(KnownSettings.RunningProcess(process.ExeName));
                }
                catch (Exception ex)
                {
                    warnings.Add($"{process.ExeName} : état inconnu, non fermé ({ex.Message}).");
                    continue;
                }

                if (!current.IsAbsent)
                {
                    Try(() => journal.Apply(SessionPlan.CloseAndRelaunch(process)),
                        $"{process.ExeName} fermé (sera relancé).", $"{process.ExeName} non fermé", applied, warnings);
                }
            }
            else
            {
                try
                {
                    if (processes.Close(process.ExeName) > 0)
                    {
                        applied.Add($"{process.ExeName} fermé.");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"{process.ExeName} non fermé : {ex.Message}");
                }
            }
        }

        if (profile.Priority != GamePriority.Normal)
        {
            if (processes.TrySetPriority(processId, profile.Priority) is { } error)
            {
                warnings.Add($"Priorité non modifiée : {error}");
            }
            else
            {
                applied.Add($"Priorité du jeu : {SessionPlan.PriorityLabel(profile.Priority)}.");
            }
        }

        return new SessionStartReport(profile, applied, warnings);
    }

    private SessionEndReport RestoreLocked(string gameName, bool afterCrash)
    {
        var restore = journal.RestoreAll();
        var warnings = new List<string>();

        // Une relance ratée (programme désinstallé, déplacé…) ne doit pas rester en attente indéfiniment.
        foreach (var failure in restore.Failed.Where(f => f.Target.Kind == KnownSettings.ProcessKind).ToList())
        {
            journal.Discard(failure.Target);
            restore.Failed.Remove(failure);
            warnings.Add($"{failure.Target.Path} n'a pas pu être relancé : {failure.Error}");
        }

        if (!journal.HasPendingEntries)
        {
            journal.SetContext(null);
        }
        _current = null;
        return new SessionEndReport(gameName, restore, warnings, afterCrash);
    }

    private static void Try(Action action, string success, string failure, List<string> applied, List<string> warnings)
    {
        try
        {
            action();
            applied.Add(success);
        }
        catch (Exception ex)
        {
            warnings.Add($"{failure} : {ex.Message}");
        }
    }

    private static string GameName(string? exePath) =>
        exePath is null ? "session précédente" : Path.GetFileNameWithoutExtension(exePath);
}
