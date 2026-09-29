using System.Windows;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Services;
using OptiGame.App.Tray;
using OptiGame.App.ViewModels;
using OptiGame.App.Views;
using OptiGame.Core;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.State;
using OptiGame.Platform;
using OptiGame.Platform.Processes;

namespace OptiGame.App;

public partial class App : Application
{
    /// <summary>Démarrage dans la zone de notification sans ouvrir la fenêtre (utilisé par le démarrage automatique).</summary>
    public const string MinimizedArgument = "--minimized";

    private SingleInstance? _singleInstance;
    private ServiceProvider? _services;
    private TaskbarIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = SingleInstance.TryAcquire(onActivationRequested: () => Dispatcher.Invoke(ShowMainWindow));
        if (_singleInstance is null)
        {
            // Une instance tourne déjà : elle a été priée de s'afficher.
            Shutdown();
            return;
        }

        _services = ConfigureServices().BuildServiceProvider();

        if (!LoadStateFiles(_services))
        {
            Shutdown();
            return;
        }

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.DataContext = _services.GetRequiredService<TrayViewModel>();
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        _services.GetRequiredService<NotificationService>().Attach(_trayIcon);

        StartSessions(_services);

        if (!e.Args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase))
        {
            ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Pas de restauration ici : si Windows s'arrête pendant une partie, le journal de session
        // est rejoué au prochain démarrage d'OptiGame.
        _trayIcon?.Dispose();
        _services?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public void ShowMainWindow()
    {
        var window = _services!.GetRequiredService<MainWindow>();
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }

    /// <summary>Récupération d'une session interrompue, puis détection des jeux.</summary>
    private static void StartSessions(IServiceProvider services)
    {
        var notifications = services.GetRequiredService<INotificationService>();
        var sessionVm = services.GetRequiredService<SessionViewModel>(); // s'abonne aux événements de session
        var sessions = services.GetRequiredService<GameSessionManager>();
        var monitor = services.GetRequiredService<GameMonitor>();
        monitor.Error += (_, message) => notifications.Show("OptiGame : erreur de détection", message, isWarning: true);

        try
        {
            var (outcome, _, pid) = sessions.Recover();
            if (outcome == RecoveryOutcome.Resumed && pid is { } gamePid && sessions.Current is { } current)
            {
                monitor.WatchSessionProcess(gamePid);
                sessionVm.NotifyResumed(current);
            }
        }
        catch (Exception ex)
        {
            notifications.Show("OptiGame : restauration impossible",
                $"La session précédente n'a pas pu être restaurée : {ex.Message}", isWarning: true);
        }

        try
        {
            monitor.Start();
        }
        catch (Exception ex)
        {
            notifications.Show("OptiGame : détection des jeux indisponible",
                $"Les profils ne seront pas appliqués automatiquement : {ex.Message}", isWarning: true);
        }
    }

    /// <summary>
    /// Charge les fichiers d'état dès le démarrage. S'ils sont illisibles, on s'arrête plutôt que de risquer
    /// d'écraser l'état d'origine qu'ils contiennent.
    /// </summary>
    private static bool LoadStateFiles(IServiceProvider services)
    {
        try
        {
            services.GetRequiredKeyedService<ChangeJournal>(JournalKeys.Fixes);
            services.GetRequiredKeyedService<ChangeJournal>(JournalKeys.Session);
            services.GetRequiredService<ProfileStore>();
            return true;
        }
        catch (StateFileCorruptException ex)
        {
            MessageBox.Show(
                $"{ex.Message}\n\nCe fichier contient des données d'OptiGame (état d'origine de réglages ou profils). " +
                "OptiGame ne démarrera pas tant qu'il est illisible, pour ne pas perdre ces informations. " +
                "Vous pouvez l'ouvrir dans un éditeur de texte pour le réparer, ou le déplacer si vous acceptez de perdre son contenu.",
                "OptiGame", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddOptiGamePlatform(AppPaths.Default);

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());

        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<DiagnosticViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
