using System.Windows;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Services;
using OptiGame.App.Tray;
using OptiGame.App.ViewModels;
using OptiGame.App.Views;
using OptiGame.Core;
using OptiGame.Core.State;
using OptiGame.Platform;

namespace OptiGame.App;

public partial class App : Application
{
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

        if (!LoadJournals(_services))
        {
            Shutdown();
            return;
        }

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.DataContext = _services.GetRequiredService<TrayViewModel>();
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);

        ShowMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

    /// <summary>
    /// Charge les journaux dès le démarrage. S'ils sont illisibles, on s'arrête plutôt que de risquer d'écraser
    /// l'état d'origine qu'ils contiennent.
    /// </summary>
    private static bool LoadJournals(IServiceProvider services)
    {
        try
        {
            services.GetRequiredKeyedService<ChangeJournal>(JournalKeys.Fixes);
            services.GetRequiredKeyedService<ChangeJournal>(JournalKeys.Session);
            return true;
        }
        catch (StateFileCorruptException ex)
        {
            MessageBox.Show(
                $"{ex.Message}\n\nCe fichier contient l'état d'origine de réglages modifiés par OptiGame. " +
                "OptiGame ne démarrera pas tant qu'il est illisible, pour ne pas perdre ces informations. " +
                "Vous pouvez l'ouvrir dans un éditeur de texte pour le réparer, ou le déplacer si vous acceptez de perdre ces sauvegardes.",
                "OptiGame", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddOptiGamePlatform(AppPaths.Default);

        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<DiagnosticViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
