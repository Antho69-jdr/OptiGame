using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Controls;
using OptiGame.App.Services;
using OptiGame.Core.Sessions;
using OptiGame.Core.Text;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Partie en cours (réglages de partie appliqués), pour la carte de la barre latérale et le menu de notification. Les
/// problèmes (détection en panne, restauration ratée, reprise après plantage) sont signalés par notification ET par une
/// alerte persistante de la fenêtre (<see cref="ShellAlerts"/>) : les notifications peuvent être masquées.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly GameSessionManager _sessions;
    private readonly INotificationService _notifications;
    private readonly ShellAlerts _alerts;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;

    public SessionViewModel(GameSessionManager sessions, INotificationService notifications, ShellAlerts alerts, IDialogService dialogs,
        TimeProvider time)
    {
        _sessions = sessions;
        _notifications = notifications;
        _alerts = alerts;
        _dialogs = dialogs;
        _time = time;
        sessions.SessionStarted += (_, report) => OnUi(() => OnStarted(report));
        sessions.SessionEnded += (_, report) => OnUi(() => OnEnded(report));
        Refresh();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndNowCommand))]
    private bool _isActive;

    /// <summary>« Partie en cours : Portal 2 (depuis 20:15) » : menu et info-bulle de l'icône de notification.</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Nom du jeu en cours (titre de la carte de la barre latérale).</summary>
    [ObservableProperty]
    private string _gameName = "";

    /// <summary>« Optimisé depuis 20:15 ».</summary>
    [ObservableProperty]
    private string _sinceText = "";

    /// <summary>Raison pour laquelle la détection des jeux ne fonctionne pas ; null si elle fonctionne.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectionError))]
    private string? _detectionError;

    public bool HasDetectionError => DetectionError is not null;

    public string TrayToolTip => IsActive ? $"OptiGame — {StatusText}" : "OptiGame";

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(TrayToolTip));

    partial void OnDetectionErrorChanged(string? value)
    {
        if (value is null)
        {
            _alerts.Dismiss("detection");
            return;
        }
        _alerts.Show(new ShellAlert("detection", Severity.Error, "Détection des jeux indisponible",
            $"Les réglages de partie ne seront pas appliqués automatiquement : {value}")
        {
            IsClosable = false,
            ShowsLog = true,
        });
    }

    /// <summary>
    /// Arrête l'optimisation tout de suite (après confirmation) et restaure les réglages d'origine ; le jeu continue de tourner,
    /// sans les réglages de partie.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task EndNowAsync()
    {
        if (_sessions.Current is not { } current) return;
        if (!_dialogs.Confirm($"Arrêter l'optimisation de {current.Profile.Name} ?",
                "OptiGame restaure maintenant les réglages d'origine. Le jeu continue de tourner, sans les réglages de partie.",
                "Arrêter et restaurer"))
        {
            return;
        }
        await Task.Run(_sessions.EndNow);
    }

    public void NotifyResumed(ActiveSession session)
    {
        const string message = "OptiGame s'était arrêté pendant la partie. Les réglages d'origine seront restaurés à la fermeture du jeu.";
        _notifications.Show($"Partie reprise : {session.Profile.Name}", message);
        _alerts.Show(new ShellAlert("session-resumed", Severity.Info, $"Partie reprise : {session.Profile.Name}", message));
    }

    /// <summary>Restauration de la partie précédente impossible au démarrage (journal de session gardé : nouvel essai au suivant).</summary>
    public void NotifyRecoveryFailed(string error)
    {
        _notifications.Show("OptiGame : restauration impossible", $"Les réglages de la partie précédente n'ont pas pu être restaurés : {error}", isWarning: true);
        _alerts.Show(new ShellAlert("session-recovery", Severity.Error, "Restauration impossible",
            "Les réglages de la partie précédente n'ont pas pu être restaurés. OptiGame réessaiera au prochain démarrage ; " +
            "le détail est dans le journal.")
        {
            ShowsLog = true,
        });
    }

    private void OnStarted(SessionStartReport report)
    {
        Refresh();
        var message = new StringBuilder();
        foreach (var line in report.Applied) message.AppendLine(line);
        foreach (var line in report.Warnings) message.AppendLine("⚠ " + line);
        if (message.Length == 0) message.Append("Rien à modifier (aucun programme à fermer n'était ouvert).");
        _notifications.Show($"Partie de {report.Profile.Name} optimisée", message.ToString().Trim(), report.Warnings.Count > 0);
        if (report.Warnings.Count > 0)
        {
            _alerts.Show(new ShellAlert("session-start", Severity.Warning, $"Partie de {report.Profile.Name} : optimisation partielle",
                string.Join("\n", report.Warnings)));
        }
    }

    private void OnEnded(SessionEndReport report)
    {
        Refresh();
        var message = new StringBuilder(report.AfterCrash
            ? "Partie interrompue (OptiGame s'était arrêté) : réglages d'origine restaurés."
            : "Réglages d'origine restaurés.");
        foreach (var failure in report.Restore.Failed)
        {
            message.AppendLine().Append($"⚠ Échec pour {failure.Target} : {failure.Error} (nouvel essai au prochain démarrage).");
        }
        foreach (var warning in report.Warnings)
        {
            message.AppendLine().Append("⚠ " + warning);
        }
        _notifications.Show($"Partie terminée : {report.GameName}", message.ToString(), !report.Success);

        _alerts.Dismiss("session-start");
        _alerts.Dismiss("session-resumed");
        if (report.Restore.Failed.Count > 0)
        {
            var count = report.Restore.Failed.Count;
            _alerts.Show(new ShellAlert("session-end", Severity.Error, $"Restauration incomplète après {report.GameName}",
                $"{FrenchText.Count(count, "réglage d'origine n'a", "réglages d'origine n'ont")} pas pu être " +
                $"{FrenchText.Agree(count, "rétabli", "rétablis")}. OptiGame réessaiera au prochain démarrage ; le détail est dans le journal.")
            {
                ShowsLog = true,
            });
        }
        else if (report.Warnings.Count > 0)
        {
            _alerts.Show(new ShellAlert("session-end", Severity.Warning, $"Partie terminée : {report.GameName}", string.Join("\n", report.Warnings)));
        }
    }

    private void Refresh()
    {
        var current = _sessions.Current;
        IsActive = current is not null;
        var since = current is null ? "" : $"{TimeZoneInfo.ConvertTime(current.StartedAt, _time.LocalTimeZone):HH:mm}";
        GameName = current?.Profile.Name ?? "";
        SinceText = current is null ? "" : $"Optimisé depuis {since}";
        StatusText = current is null ? "" : $"Partie en cours : {current.Profile.Name} (depuis {since})";
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
