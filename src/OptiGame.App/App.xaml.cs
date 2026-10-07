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
using OptiGame.Core.Settings;
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

        if (Snapshots.PageSnapshots.IsRequested(e.Args))
        {
            RunSnapshots(e.Args);
            return;
        }

        _singleInstance = SingleInstance.TryAcquire(onActivationRequested: () => Dispatcher.Invoke(ShowMainWindow));
        if (_singleInstance is null)
        {
            // Une instance tourne déjà : elle a été priée de s'afficher.
            Shutdown();
            return;
        }

        _services = ConfigureServices().BuildServiceProvider();
        UiMotion.Attach(_services.GetRequiredService<AppSettingsStore>());
        Controls.TrailerPlayer.Attach(_services.GetRequiredService<FileLog>(), Path.Combine(_services.GetRequiredService<AppPaths>().Root, "webview"));
        CoverAppearance.Attach(_services.GetRequiredService<AppSettingsStore>());
        var services = _services;

        // Filet de sécurité : une erreur dans l'interface (ex. le dock) ne doit pas arrêter OptiGame pendant une partie.
        // Elle est journalisée avec sa pile d'appels et signalée ; l'appli continue.
        DispatcherUnhandledException += (_, args) =>
        {
            services.GetRequiredService<FileLog>().Error($"Erreur d'interface inattendue :{Environment.NewLine}{args.Exception}");
            services.GetRequiredService<INotificationService>().Show("OptiGame : erreur inattendue",
                $"{args.Exception.Message} (détails dans le journal).", isWarning: true);
            services.GetRequiredService<ShellAlerts>().Show(new ShellAlert("ui-error", Controls.Severity.Error, "Erreur inattendue de l'interface",
                "OptiGame continue de fonctionner. Si l'erreur se répète, le détail est dans le journal.") { ShowsLog = true });
            args.Handled = true;
        };

        if (!LoadStateFiles(_services))
        {
            Shutdown();
            return;
        }

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.Icon = LoadTrayIcon(_services.GetRequiredService<FileLog>());
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
        _services.GetRequiredService<UpdateService>().Start(this);

        _quitWatcher = new QuitRequestWatcher(services.GetRequiredService<AppPaths>().Root,
            () => Dispatcher.BeginInvoke(async () =>
            {
                services.GetRequiredService<FileLog>().Info("Arrêt demandé (quit.request : installeur d'OptiGame ou scripts\\dev-run.ps1).");
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

    /// <summary>
    /// Captures des pages pour le développement (Snapshots/PageSnapshots) : services et état chargés, rien d'autre ne démarre
    /// (ni instance unique, ni détection, ni reprise de session, ni dock, ni mise à jour). Seulement avec OPTIGAME_DATA_DIR.
    /// </summary>
    private async void RunSnapshots(string[] args)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPTIGAME_DATA_DIR")))
        {
            Shutdown(2);
            return;
        }
        _services = ConfigureServices().BuildServiceProvider();
        CoverAppearance.Attach(_services.GetRequiredService<AppSettingsStore>()); // captures fidèles au réglage
        Controls.TrailerPlayer.Attach(_services.GetRequiredService<FileLog>(), Path.Combine(_services.GetRequiredService<AppPaths>().Root, "webview"));
        var services = _services;
        DispatcherUnhandledException += (_, a) =>
        {
            services.GetRequiredService<FileLog>().Error($"Captures : erreur d'interface :{Environment.NewLine}{a.Exception}");
            a.Handled = true;
        };
        if (!LoadStateFiles(services))
        {
            Shutdown(1);
            return;
        }
        await Snapshots.PageSnapshots.RunAsync(services, args);
        Shutdown();
    }

    /// <summary>
    /// Logo à la taille des icônes de la zone de notification pour l'échelle d'affichage (16 px à 100 %, 20 à 125 %, 24 à
    /// 150 %) : OptiGame.ico contient une image dessinée à chacune de ces tailles, rien n'est étiré.
    /// </summary>
    private static System.Drawing.Icon? LoadTrayIcon(FileLog log)
    {
        try
        {
            var size = Platform.Display.IconMetrics.SmallIconPixels();
            var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/OptiGame.ico"))
                ?? throw new IOException("Logo absent des ressources (Assets/OptiGame.ico).");
            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, size, size);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Jamais d'arrêt au démarrage pour une icône : celle de l'exe, moins nette, fait l'affaire.
            log.Error("Logo de la zone de notification illisible", ex);
            return Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null;
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

    /// <summary>Fenêtre principale affichée, même réduite : l'utilisateur s'en sert (une mise à jour automatique attend).</summary>
    public bool IsMainWindowShown => _mainWindow is { IsVisible: true };

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
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (_mainWindowPlacement is { } placement)
        {
            window.Left = placement.Bounds.Left;
            window.Top = placement.Bounds.Top;
            window.Width = placement.Bounds.Width;
            window.Height = placement.Bounds.Height;
            window.WindowState = placement.State; // réduite au début de la partie : revient réduite
        }
        else
        {
            // Place de la dernière fermeture (settings.json), si elle est encore visible ; sinon 90 % de la zone de travail au plus.
            var saved = _services!.GetRequiredService<AppSettingsStore>().Get().MainWindowPlacement;
            var area = SystemParameters.WorkArea;
            var bounds = WindowLayout.InitialBounds(saved,
                new ScreenRect(area.Left, area.Top, area.Width, area.Height),
                new ScreenRect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight),
                window.MinWidth, window.MinHeight);
            (window.Left, window.Top, window.Width, window.Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (saved is { Maximized: true } && bounds.Left == saved.Left && bounds.Top == saved.Top)
            {
                window.WindowState = WindowState.Maximized;
            }
        }
        window.PlacementSaving += (_, _) => SavePlacement(window);
        window.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not false) return;
            _services?.GetRequiredService<UpdateService>().MainWindowHidden();
            // Première fermeture : dire qu'OptiGame continue en arrière-plan. Différé : pendant l'arrêt de l'appli (Quitter),
            // le répartiteur s'arrête avant de l'exécuter, et une fermeture pour une partie ne compte pas.
            if (!window.IsClosingForGame) Dispatcher.BeginInvoke(ExplainCloseToTray, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_mainWindow, window)) _mainWindow = null;
            // Application.MainWindow (propriétaire des dialogues) ne doit pas garder en vie une fenêtre fermée.
            if (ReferenceEquals(MainWindow, window)) MainWindow = null;
        };
        MainWindow = window;
        return _mainWindow = window;
    }

    /// <summary>Place de la fenêtre (hors état agrandi) gardée dans settings.json, si elle a changé.</summary>
    private void SavePlacement(MainWindow window)
    {
        var bounds = window.RestoreBounds;
        if (bounds.IsEmpty || _services is null) return;
        var settings = _services.GetRequiredService<AppSettingsStore>();
        var maximized = window.WindowState == WindowState.Maximized;
        if (settings.Get().MainWindowPlacement is { } old && old.Left == bounds.Left && old.Top == bounds.Top &&
            old.Width == bounds.Width && old.Height == bounds.Height && old.Maximized == maximized)
        {
            return;
        }
        settings.Update(s => s.MainWindowPlacement = new WindowPlacement
        {
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Maximized = maximized,
        });
    }

    /// <summary>
    /// Première fermeture de la fenêtre : OptiGame reste dans la zone de notification (il applique les réglages de partie au
    /// lancement des jeux). Dit une seule fois, avec le moyen de le quitter vraiment.
    /// </summary>
    private void ExplainCloseToTray()
    {
        if (_services is null || _mainWindow is { IsVisible: true }) return;
        var settings = _services.GetRequiredService<AppSettingsStore>();
        if (settings.Get().CloseToTrayExplained) return;
        settings.Update(s => s.CloseToTrayExplained = true);
        var quit = _services.GetRequiredService<IDialogService>().Confirm("OptiGame reste actif",
            "Il continue dans la zone de notification pour optimiser vos jeux dès leur lancement. Pour le rouvrir, cliquez sur son " +
            "icône ; pour le quitter, clic droit sur l'icône puis « Quitter ».",
            "Quitter OptiGame", cancelLabel: "Continuer en arrière-plan");
        if (quit) _services.GetRequiredService<TrayViewModel>().ExitCommand.Execute(null);
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
        log.Info($"OptiGame {AppInfo.Version} démarre ({Environment.ProcessPath}).");
        var notifications = services.GetRequiredService<INotificationService>();
        var sessionVm = services.GetRequiredService<SessionViewModel>(); // s'abonne aux événements de session
        var sessions = services.GetRequiredService<GameSessionManager>();
        var monitor = services.GetRequiredService<GameMonitor>();
        monitor.Error += (_, message) =>
        {
            notifications.Show("OptiGame : erreur de détection", message, isWarning: true);
            Current.Dispatcher.BeginInvoke(() => sessionVm.DetectionError = message);
        };
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
            log.Error("Restauration de la partie précédente impossible", ex);
            sessionVm.NotifyRecoveryFailed(ex.Message);
        }
        playtime.ReconcileAtStartup();

        try
        {
            monitor.Start();
        }
        catch (Exception ex)
        {
            // Les notifications peuvent être masquées par Windows : l'erreur est aussi affichée dans la fenêtre.
            sessionVm.DetectionError = ex.Message;
            notifications.Show("OptiGame : détection des jeux indisponible",
                $"Les réglages de partie ne seront pas appliqués automatiquement : {ex.Message}", isWarning: true);
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
        services.AddSingleton<ShellAlerts>();
        services.AddSingleton<UnsavedChangesGuard>();

        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<DiagnosticViewModel>();
        services.AddSingleton<DriversViewModel>();
        services.AddSingleton<GameGraphicsService>();
        services.AddSingleton<FrameCapService>();
        services.AddSingleton<InGameQualityService>();
        services.AddSingleton<DlssOverrideService>();
        services.AddSingleton<GameRatingService>();
        services.AddSingleton<NewSteamGamesViewModel>();
        services.AddSingleton<GameTagService>();
        services.AddSingleton<LaunchersViewModel>();
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
        services.AddSingleton<UpdateService>();
        // Transitoire : fermée pendant les parties et recréée ensuite (une seule à la fois, gardée par App._mainWindow).
        services.AddTransient<MainWindow>();

        return services;
    }
}
