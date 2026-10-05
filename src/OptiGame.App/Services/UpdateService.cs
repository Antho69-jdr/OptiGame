using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.Core.Logging;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Core.Updates;
using OptiGame.Platform.Display;
using OptiGame.Platform.Drivers;
using OptiGame.Platform.Privileged;
using OptiGame.Platform.Updates;

namespace OptiGame.App.Services;

public enum UpdatePhase
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,

    /// <summary>Installeur téléchargé et vérifié, en attente d'être lancé.</summary>
    Ready,
    Installing,
    Failed,
}

/// <summary>
/// Mises à jour d'OptiGame (versions publiées sur GitHub) : recherche 2 min après le démarrage puis toutes les 24 h, reportée
/// pendant les parties ; téléchargement vérifié ; installation d'elle-même en mode automatique (aucun jeu, fenêtre fermée, pas
/// de plein écran), sinon sur un clic. Seule la copie installée dans Program Files se met à jour (<see cref="InstalledCopy"/>).
/// Une mise à jour ne change que les fichiers d'OptiGame, jamais un réglage de Windows. Thread UI.
/// </summary>
public sealed partial class UpdateService : ObservableObject
{
    private readonly AppUpdateClient _client;
    private readonly AppSettingsStore _settings;
    private readonly IPrivilegedOperations _privileged;
    private readonly GameSessionManager _sessions;
    private readonly GameTimeGate _gate;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;
    private readonly FileLog _log;
    private readonly DispatcherTimer _checkTimer = new() { Interval = UpdatePolicy.FirstCheckDelay };
    private readonly DispatcherTimer _retryTimer = new() { Interval = UpdatePolicy.RetryDelay };
    private App? _app;
    private string? _installerPath;
    private Version? _notified;

    public UpdateService(AppUpdateClient client, AppSettingsStore settings, IPrivilegedOperations privileged, GameSessionManager sessions,
        GameTimeGate gate, INotificationService notifications, IDialogService dialogs, FileLog log)
    {
        _client = client;
        _settings = settings;
        _privileged = privileged;
        _sessions = sessions;
        _gate = gate;
        _notifications = notifications;
        _dialogs = dialogs;
        _log = log;
        _checkTimer.Tick += (_, _) =>
        {
            _checkTimer.Interval = UpdatePolicy.CheckInterval; // ensuite une fois par jour
            if (Mode != UpdateMode.Off) _gate.RunOrDefer("update-check", () => _ = CheckAsync(manual: false));
        };
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer.Stop();
            _retryTimer.Interval = UpdatePolicy.RetryDelay;
            TryInstallAutomatically();
        };
    }

    public Version Current { get; } = UpdatePolicy.Parse(AppInfo.Version) ?? new Version(0, 0, 0);

    public string CurrentText => Current.ToString(3);

    /// <summary>Raison pour laquelle cette copie ne s'installe pas de mise à jour elle-même ; null = copie installée.</summary>
    public string? SelfUpdateBlocker { get; private set; }

    public bool CanSelfUpdate => SelfUpdateBlocker is null;

    private UpdateMode Mode => _settings.Get().UpdateMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsDownloading), nameof(IsAvailableBannerVisible), nameof(AvailableBannerText),
        nameof(AvailableBannerTitle), nameof(AvailableBannerSeverity), nameof(CanDismissAvailableBanner))]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand), nameof(InstallNowCommand))]
    private UpdatePhase _phase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableBannerText))]
    private string _statusText = "";

    /// <summary>Dernière version trouvée, plus récente que celle qui tourne ; null sinon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackage), nameof(IsAvailableBannerVisible), nameof(AvailableBannerText), nameof(AvailableBannerTitle))]
    [NotifyCanExecuteChangedFor(nameof(InstallNowCommand))]
    private UpdatePackage? _package;

    /// <summary>Avancement du téléchargement, de 0 à 100.</summary>
    [ObservableProperty]
    private double _downloadPercent;

    /// <summary>Version installée par une mise à jour juste avant ce démarrage (bandeau « mis à jour ») ; null sinon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdatedBannerVisible))]
    private string? _justUpdatedTo;

    public bool HasPackage => Package is not null;

    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;

    public bool IsDownloading => Phase == UpdatePhase.Downloading;

    /// <summary>Version dont l'utilisateur a fermé le bandeau (« Plus tard ») : plus proposée dans la fenêtre avant la suivante.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailableBannerVisible))]
    private Version? _dismissedVersion;

    /// <summary>
    /// Bandeau de la fenêtre : version trouvée, téléchargement (lancé depuis le bandeau) puis installation. Fermé par « Plus
    /// tard » tant que rien n'est en cours ; un échec s'affiche toujours.
    /// </summary>
    public bool IsAvailableBannerVisible => Package is { } package
        && Phase is not (UpdatePhase.Idle or UpdatePhase.Checking or UpdatePhase.UpToDate)
        && !(Phase is UpdatePhase.Available or UpdatePhase.Ready && package.Version == DismissedVersion);

    public bool IsUpdatedBannerVisible => JustUpdatedTo is not null;

    /// <summary>« Plus tard » possible tant que rien n'est en cours (version trouvée ou prête).</summary>
    public bool CanDismissAvailableBanner => Phase is UpdatePhase.Available or UpdatePhase.Ready;

    /// <summary>Échec = erreur (rouge) ; sinon information.</summary>
    public Controls.Severity AvailableBannerSeverity => Phase == UpdatePhase.Failed ? Controls.Severity.Error : Controls.Severity.Info;

    public string AvailableBannerTitle => Package is not { } package ? ""
        : Phase switch
        {
            UpdatePhase.Failed => "Mise à jour impossible pour l'instant",
            UpdatePhase.Downloading => $"Téléchargement d'OptiGame {package.Version.ToString(3)}",
            UpdatePhase.Installing => $"Installation d'OptiGame {package.Version.ToString(3)}",
            UpdatePhase.Ready => $"OptiGame {package.Version.ToString(3)} est prêt",
            _ => $"OptiGame {package.Version.ToString(3)} est disponible",
        };

    public string AvailableBannerText => Package is null ? ""
        : Phase is UpdatePhase.Downloading or UpdatePhase.Installing or UpdatePhase.Failed ? StatusText
        : !CanSelfUpdate ? "Installez-le depuis sa page de téléchargement."
        : Phase == UpdatePhase.Ready && Mode == UpdateMode.Automatic
            ? "Il s'installera de lui-même peu après la fermeture de cette fenêtre : OptiGame redémarre en quelques secondes."
        : "";

    /// <summary>« Plus tard » : le bandeau ne revient qu'avec une version plus récente (Paramètres > Mises à jour reste disponible).</summary>
    [RelayCommand]
    private void DismissAvailableBanner() => DismissedVersion = Package?.Version;

    /// <summary>Au démarrage de l'appli : mise à jour tout juste installée, nettoyage, puis recherches automatiques.</summary>
    public void Start(App app)
    {
        _app = app;
        SelfUpdateBlocker = InstalledCopy.WhyNoSelfUpdate();
        OnPropertyChanged(nameof(CanSelfUpdate));
        InstallNowCommand.NotifyCanExecuteChanged();

        var settings = _settings.Get();
        if (UpdatePolicy.JustUpdated(settings.LastRunVersion, Current))
        {
            JustUpdatedTo = CurrentText;
            _log.Info($"Mise à jour installée : {settings.LastRunVersion} → {CurrentText}.");
            _notifications.Show("OptiGame est à jour", $"Version {CurrentText} installée.");
        }
        if (settings.LastRunVersion != CurrentText) _settings.Update(s => s.LastRunVersion = CurrentText);

        if (CanSelfUpdate)
        {
            try
            {
                _client.CleanUp(InstalledCopy.UpdatesFolder, Current); // installeur de la version qui tourne : il a servi
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"Nettoyage du dossier des mises à jour impossible : {ex.Message}");
            }
            _log.Info($"Mises à jour : copie installée ({AppContext.BaseDirectory}), {ModeLabel(settings.UpdateMode)}.");
        }
        else
        {
            _log.Info($"Mises à jour : pas d'installation automatique pour cette copie — {SelfUpdateBlocker}.");
        }

        StatusText = settings.UpdateMode == UpdateMode.Off
            ? "Recherche automatique désactivée."
            : "Recherche automatique : quelques minutes après le démarrage, puis une fois par jour.";
        if (settings.UpdateMode != UpdateMode.Off) _checkTimer.Start();
    }

    /// <summary>
    /// Fenêtre principale fermée (masquée) : une mise à jour prête qui l'attendait s'installe peu après. Pas tout de suite : la
    /// fenêtre se ferme aussi quand on quitte OptiGame, et l'appli arrêtée entre-temps ne lance plus rien.
    /// </summary>
    public void MainWindowHidden()
    {
        if (Phase != UpdatePhase.Ready || Mode != UpdateMode.Automatic) return;
        _retryTimer.Stop();
        _retryTimer.Interval = UpdatePolicy.AfterWindowClosedDelay;
        _retryTimer.Start();
    }

    /// <summary>Mode changé dans les Paramètres.</summary>
    public void ModeChanged()
    {
        var mode = Mode;
        _log.Info($"Mises à jour : {ModeLabel(mode)}.");
        if (mode == UpdateMode.Off)
        {
            _checkTimer.Stop();
            _retryTimer.Stop();
            if (!IsBusy) StatusText = "Recherche automatique désactivée.";
        }
        else if (!_checkTimer.IsEnabled)
        {
            _checkTimer.Interval = UpdatePolicy.FirstCheckDelay;
            _checkTimer.Start();
        }
        OnPropertyChanged(nameof(AvailableBannerText));
        if (mode == UpdateMode.Automatic && Phase == UpdatePhase.Ready) TryInstallAutomatically();
    }

    private bool CanCheckNow() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private Task CheckNowAsync() => CheckAsync(manual: true);

    private bool CanInstallNow() => Package is not null && CanSelfUpdate && Phase is UpdatePhase.Available or UpdatePhase.Ready or UpdatePhase.Failed;

    /// <summary>« Installer maintenant » (Paramètres ou bandeau) : OptiGame redémarre ensuite fenêtre ouverte.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallNow))]
    private async Task InstallNowAsync()
    {
        if (_sessions.Current is not null)
        {
            _dialogs.ShowInfo("Partie en cours", "Vous pourrez installer la mise à jour une fois la partie terminée.");
            return;
        }
        if (Phase != UpdatePhase.Ready && !await DownloadAsync()) return;
        Install(showWindowAfter: true);
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        var page = Package?.PageUrl ?? (JustUpdatedTo is not null ? AppReleases.PageFor(Current) : new Uri($"https://github.com/{AppReleases.Owner}/{AppReleases.Repository}/releases"));
        Process.Start(new ProcessStartInfo("explorer.exe", page.ToString()) { UseShellExecute = true });
    }

    [RelayCommand]
    private void DismissUpdatedBanner() => JustUpdatedTo = null;

    private async Task CheckAsync(bool manual)
    {
        if (IsBusy) return;
        Phase = UpdatePhase.Checking;
        StatusText = "Recherche d'une mise à jour…";
        try
        {
            var result = await _client.CheckAsync(Current);
            _log.Info($"Mises à jour : {result.Message}");
            StatusText = result.Message;
            if (result.Package is not { } package)
            {
                Package = null;
                Phase = UpdatePhase.UpToDate;
                return;
            }

            Package = package;
            Phase = UpdatePhase.Available;
            if (!CanSelfUpdate)
            {
                StatusText = $"{result.Message} Cette copie ne s'installe pas de mise à jour elle-même : {SelfUpdateBlocker}.";
                return;
            }
            if (Mode == UpdateMode.Automatic)
            {
                if (await DownloadAsync()) TryInstallAutomatically();
            }
            else if (!manual && _notified != package.Version)
            {
                _notified = package.Version;
                _notifications.Show("Mise à jour d'OptiGame", $"La version {package.Version.ToString(3)} est disponible : Paramètres > Mises à jour pour l'installer.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            Phase = UpdatePhase.Failed;
            StatusText = $"Recherche impossible pour l'instant (connexion ?) : {ex.Message}";
            _log.Warn($"Mises à jour : recherche impossible — {ex.Message}");
        }
    }

    private async Task<bool> DownloadAsync()
    {
        if (Package is not { } package) return false;
        Phase = UpdatePhase.Downloading;
        DownloadPercent = 0;
        StatusText = $"Téléchargement d'OptiGame {package.Version.ToString(3)}…";
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                DownloadPercent = p.Total is long total && total > 0 ? 100.0 * p.Received / total : 0;
                StatusText = $"Téléchargement d'OptiGame {package.Version.ToString(3)} : {p.Received / 1048576.0:N0} / {package.InstallerSize / 1048576.0:N0} Mo";
            });
            _installerPath = await _client.DownloadAsync(package, InstalledCopy.UpdatesFolder, progress, CancellationToken.None);
            Phase = UpdatePhase.Ready;
            StatusText = Mode == UpdateMode.Automatic
                ? $"OptiGame {package.Version.ToString(3)} est prêt : il s'installera dès qu'aucun jeu ne tournera et que la fenêtre sera fermée."
                : $"OptiGame {package.Version.ToString(3)} est prêt à être installé.";
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InstallerRejectedException)
        {
            Phase = UpdatePhase.Failed;
            StatusText = $"Téléchargement de la mise à jour impossible : {ex.Message}";
            _log.Error($"Mise à jour {package.Version.ToString(3)} : téléchargement impossible", ex);
            return false;
        }
    }

    /// <summary>Mode automatique : installe si le moment convient, sinon réessaie un peu plus tard.</summary>
    private void TryInstallAutomatically()
    {
        if (Phase != UpdatePhase.Ready || Mode != UpdateMode.Automatic || !CanSelfUpdate) return;
        var why = UpdatePolicy.WhyNotNow(_sessions.Current is not null, _app?.IsMainWindowShown == true, FullscreenWatcher.IsFullscreenNow());
        if (why is not null)
        {
            _retryTimer.Start();
            return;
        }
        Install(showWindowAfter: false);
    }

    private void Install(bool showWindowAfter)
    {
        if (Package is not { } package || _installerPath is null) return;
        Phase = UpdatePhase.Installing;
        StatusText = $"Installation d'OptiGame {package.Version.ToString(3)} : OptiGame redémarre…";
        try
        {
            _privileged.StartAppUpdate(_installerPath, package.Sha256, showWindowAfter);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InstallerRejectedException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Phase = UpdatePhase.Failed;
            StatusText = $"Installation de la mise à jour impossible : {ex.Message}";
            _log.Error($"Mise à jour {package.Version.ToString(3)} : installation impossible", ex);
            return;
        }
        _log.Info($"Mise à jour {package.Version.ToString(3)} : installeur lancé ({_installerPath}) ; OptiGame se ferme, il sera relancé.");
        Application.Current.Shutdown(); // l'installeur attend la fermeture d'OptiGame, le remplace puis le relance
    }

    private static string ModeLabel(UpdateMode mode) => mode switch
    {
        UpdateMode.Automatic => "installation automatique",
        UpdateMode.Notify => "prévenir seulement",
        _ => "recherche automatique désactivée",
    };
}
