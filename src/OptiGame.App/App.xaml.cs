using System.Windows;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Services;
using OptiGame.App.Tray;
using OptiGame.App.ViewModels;
using OptiGame.App.Views;
using OptiGame.Core;
using OptiGame.Core.Logging;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.State;
using OptiGame.Platform;
using OptiGame.Platform.Processes;
using OptiGame.Platform.Startup;

namespace OptiGame.App;

public partial class App : Application
{
    /// <summary>Démarrage dans la zone de notification sans ouvrir la fenêtre (utilisé par le démarrage automatique).</summary>
    public const string MinimizedArgument = "--minimized";

    private SingleInstance? _singleInstance;
    private ServiceProvider? _services;
    private TaskbarIcon? _trayIcon;
    private QuitRequestWatcher? _quitWatcher;

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
        var services = _services;

        // Filet de sécurité : une erreur dans l'interface (ex. le dock) ne doit pas arrêter OptiGame pendant une partie.
        // Elle est journalisée avec sa pile d'appels et signalée ; l'appli continue.
        DispatcherUnhandledException += (_, args) =>
        {
            services.GetRequiredService<FileLog>().Error($"Erreur d'interface inattendue :{Environment.NewLine}{args.Exception}");
            services.GetRequiredService<INotificationService>().Show("OptiGame : erreur inattendue",
                $"{args.Exception.Message} (détails dans le journal).", isWarning: true);
            args.Handled = true;
        };

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
        _services.GetRequiredService<Dock.DockController>().Start();
        _services.GetRequiredService<Platform.Measurement.AutoCapture>().Start();

        _quitWatcher = new QuitRequestWatcher(services.GetRequiredService<AppPaths>().Root,
            () => Dispatcher.BeginInvoke(async () =>
            {
                services.GetRequiredService<FileLog>().Info("Arrêt demandé par scripts\\dev-run.ps1 (quit.request).");
                // Comme « Quitter » pendant une partie : on restaure plutôt que de laisser la session en suspens.
                var sessions = services.GetRequiredService<GameSessionManager>();
                if (sessions.Current is not null)
                {
                    await Task.Run(sessions.EndNow);
                }
                Shutdown();
            }));

        if (!e.Args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase))
        {
            ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Pas de restauration ici : si Windows s'arrête pendant une partie, le journal de session
        // est rejoué au prochain démarrage d'OptiGame.
        _quitWatcher?.Dispose();
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
        var log = services.GetRequiredService<FileLog>();
        log.Info($"OptiGame démarre ({Environment.ProcessPath}).");
        var notifications = services.GetRequiredService<INotificationService>();
        var sessionVm = services.GetRequiredService<SessionViewModel>(); // s'abonne aux événements de session
        var sessions = services.GetRequiredService<GameSessionManager>();
        var monitor = services.GetRequiredService<GameMonitor>();
        monitor.Error += (_, message) => notifications.Show("OptiGame : erreur de détection", message, isWarning: true);
        // Avant Recover : le temps de jeu doit entendre la fin d'une session interrompue par un crash.
        var playtime = services.GetRequiredService<Core.Playtime.PlaytimeTracker>();

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
        playtime.ReconcileAtStartup();

        try
        {
            monitor.Start();
        }
        catch (Exception ex)
        {
            // Les notifications peuvent être masquées par Windows : l'erreur est aussi affichée dans la fenêtre.
            sessionVm.DetectionError = $"Détection des jeux indisponible : les profils ne seront pas appliqués automatiquement ({ex.Message}).";
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
            services.GetRequiredService<Core.Settings.AppSettingsStore>();
            services.GetRequiredService<Core.Measurement.CaptureStore>();
            services.GetRequiredService<Core.Playtime.PlaytimeStore>();
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
        services.AddSingleton<NavigationService>();

        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<DiagnosticViewModel>();
        services.AddSingleton<DriversViewModel>();
        services.AddSingleton<GameGraphicsService>();
        services.AddSingleton<FrameCapService>();
        services.AddSingleton<GameRatingService>();
        services.AddSingleton<NewSteamGamesViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MeasuresViewModel>();
        services.AddSingleton<DockViewModel>();
        services.AddSingleton<Dock.DockController>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
