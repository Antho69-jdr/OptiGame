using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Sessions;

namespace OptiGame.App.ViewModels;

/// <summary>État de la session de jeu en cours, pour le bandeau de la fenêtre et le menu de notification.</summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly GameSessionManager _sessions;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _time;

    public SessionViewModel(GameSessionManager sessions, INotificationService notifications, TimeProvider time)
    {
        _sessions = sessions;
        _notifications = notifications;
        _time = time;
        sessions.SessionStarted += (_, report) => OnUi(() => OnStarted(report));
        sessions.SessionEnded += (_, report) => OnUi(() => OnEnded(report));
        Refresh();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndNowCommand))]
    private bool _isActive;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Raison pour laquelle la détection des jeux ne fonctionne pas ; null si elle fonctionne.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectionError))]
    private string? _detectionError;

    public bool HasDetectionError => DetectionError is not null;

    public string TrayToolTip => IsActive ? $"OptiGame — {StatusText}" : "OptiGame";

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(TrayToolTip));

    /// <summary>Termine la session tout de suite et restaure les réglages (le jeu continue sans le profil).</summary>
    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task EndNowAsync()
    {
        await Task.Run(_sessions.EndNow);
    }

    public void NotifyResumed(ActiveSession session) =>
        _notifications.Show($"Session reprise : {session.Profile.Name}",
            "OptiGame s'était arrêté pendant la partie. Les réglages seront restaurés à la fermeture du jeu.");

    private void OnStarted(SessionStartReport report)
    {
        Refresh();
        var message = new StringBuilder();
        foreach (var line in report.Applied) message.AppendLine(line);
        foreach (var line in report.Warnings) message.AppendLine("⚠ " + line);
        if (message.Length == 0) message.Append("Rien à modifier (aucun programme à fermer n'était ouvert).");
        _notifications.Show($"Profil « {report.Profile.Name} » appliqué", message.ToString().Trim(), report.Warnings.Count > 0);
    }

    private void OnEnded(SessionEndReport report)
    {
        Refresh();
        var message = new StringBuilder(report.AfterCrash
            ? "Session interrompue (OptiGame s'était arrêté) : réglages d'origine restaurés."
            : "Réglages d'origine restaurés.");
        foreach (var failure in report.Restore.Failed)
        {
            message.AppendLine().Append($"⚠ Échec pour {failure.Target} : {failure.Error} (nouvel essai au prochain démarrage).");
        }
        foreach (var warning in report.Warnings)
        {
            message.AppendLine().Append("⚠ " + warning);
        }
        _notifications.Show($"Session terminée : {report.GameName}", message.ToString(), !report.Success);
    }

    private void Refresh()
    {
        var current = _sessions.Current;
        IsActive = current is not null;
        StatusText = current is null
            ? ""
            : $"Session en cours : {current.Profile.Name} (depuis {TimeZoneInfo.ConvertTime(current.StartedAt, _time.LocalTimeZone):HH:mm})";
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
