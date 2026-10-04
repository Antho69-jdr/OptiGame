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
    private RequestFileWatcher? _memoryWatcher;

    /// <summary>Fenêtre principale ; null quand elle n'existe pas (jamais ouverte, ou fermée pendant une partie).</summary>
    private MainWindow? _mainWindow;

    /// <summary>Place de la fenêtre fermée pendant une partie, rendue à la suivante (sinon elle reviendrait centrée).</summary>
    private (Rect Bounds, WindowState State)? _mainWindowPlacement;

    /// <summary>Fichier à créer dans le dossier de données pour écrire la mesure de la mémoire dans le journal.</summary>
    public const string MemoryRequestFile = "memory.request";

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

        // Avant la détection des jeux : aucune partie ne doit commencer sans que l'appli s'allège.
        var footprint = _services.GetRequiredService<InGameFootprint>();
        footprint.Start(this);
        StartSessions(_services);
        footprint.EnterGame(this); // partie reprise après un plantage d'OptiGame (sans quoi : rien à faire)
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

        _memoryWatcher = new RequestFileWatcher(services.GetRequiredService<AppPaths>().Root, MemoryRequestFile,
            () => Dispatcher.BeginInvoke(() => WriteMemoryReport(services)));

        if (!e.Args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase))
        {
            LogFirstRender(GetMainWindow(), services);
            ShowMainWindow();
        }
    }

    /// <summary>Durée du lancement jusqu'au premier affichage de la fenêtre (journal), pour mesurer les gains de démarrage.</summary>
    private static void LogFirstRender(MainWindow window, IServiceProvider services)
    {
        void OnRendered(object? sender, EventArgs args)
        {
            window.ContentRendered -= OnRendered;
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var elapsed = DateTime.Now - process.StartTime;
            services.GetRequiredService<FileLog>().Info(
                $"Fenêtre affichée {elapsed.TotalMilliseconds:0} ms après le lancement — mémoire : {MemoryUsage.Now().Describe()}");
        }
        window.ContentRendered += OnRendered;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Pas de restauration ici : si Windows s'arrête pendant une partie, le journal de session
        // est rejoué au prochain démarrage d'OptiGame.
        _quitWatcher?.Dispose();
        _memoryWatcher?.Dispose();
        _trayIcon?.Dispose();
        _services?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Mesure de la mémoire à la demande (fichier memory.request), écrite dans le journal : répartition actuelle, ce qui est
    /// affiché, puis la même mesure après un passage complet du ramasse-miettes .NET. L'écart entre les deux = déchets pas encore
    /// récupérés (lectures temporaires) ; ce qui reste hors .NET = WPF, images décodées, pilotes.
    /// </summary>
    private static void WriteMemoryReport(IServiceProvider services)
    {
        var log = services.GetRequiredService<FileLog>();
        log.Info($"Mémoire (demandée) : {MemoryUsage.Now().Describe()}");
        log.Info($"Mémoire, détail : {services.GetRequiredService<LibraryViewModel>().DescribeForMemoryReport()}");
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        log.Info($"Mémoire après nettoyage complet : {MemoryUsage.Now().Describe()}");
    }

    public void ShowMainWindow()
    {
        var window = GetMainWindow();
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }

    /// <summary>Fenêtre principale, créée si besoin (première ouverture, ou après une partie) à la place qu'elle avait.</summary>
    private MainWindow GetMainWindow()
    {
        if (_mainWindow is { } existing) return existing;
        var window = _services!.GetRequiredService<MainWindow>();
        if (_mainWindowPlacement is { } placement)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = placement.Bounds.Left;
            window.Top = placement.Bounds.Top;
            window.Width = placement.Bounds.Width;
            window.Height = placement.Bounds.Height;
            window.WindowState = placement.State; // réduite au début de la partie : revient réduite
        }
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_mainWindow, window)) _mainWindow = null;
            // Application.MainWindow (propriétaire des dialogues) ne doit pas garder en vie une fenêtre fermée.
            if (ReferenceEquals(MainWindow, window)) MainWindow = null;
        };
        MainWindow = window;
        return _mainWindow = window;
    }

    /// <summary>
    /// Début de partie : la fenêtre principale est FERMÉE (et non masquée), pour que tout son contenu soit libéré. Elle est
    /// recréée à la demande, même page (MainViewModel) et même place. Pas si une boîte de dialogue est ouverte : on ne
    /// l'interrompt pas. Renvoie vrai si la fenêtre était affichée (elle reviendra à la fin de la partie).
    /// </summary>
    public bool CloseMainWindowForGame()
    {
        if (_mainWindow is not { } window) return false;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal || window.OwnedWindows.Count > 0)
        {
            _services?.GetRequiredService<FileLog>().Info("Partie en cours : fenêtre d'OptiGame gardée (une boîte de dialogue est ouverte).");
            return false;
        }
        var wasVisible = window.IsVisible;
        var bounds = window.RestoreBounds;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        // Jamais affichée, ou hors des écrans actuels (écran débranché) : elle reviendra centrée.
        _mainWindowPlacement = !bounds.IsEmpty && bounds.IntersectsWith(screen) ? (bounds, window.WindowState) : null;
        window.CloseForGame();
        return wasVisible;
    }

    /// <summary>
    /// Fin de partie : la fenêtre fermée pour la partie revient comme elle était (réduite, elle reste dans la barre des tâches).
    /// Rouverte (puis peut-être refermée) par l'utilisateur pendant la partie : son dernier geste compte, on n'y touche pas.
    /// </summary>
    public void ReopenMainWindowAfterGame()
    {
        if (_mainWindow is not null) return;
        var window = GetMainWindow();
        window.Show();
        if (window.WindowState != WindowState.Minimized) window.Activate();
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
        services.AddSingleton<GameTimeGate>();
        services.AddSingleton<MemoryRelief>();
        services.AddSingleton<InGameFootprint>();
        // Transitoire : fermée pendant les parties et recréée ensuite (une seule à la fois, gardée par App._mainWindow).
        services.AddTransient<MainWindow>();

        return services;
    }
}
