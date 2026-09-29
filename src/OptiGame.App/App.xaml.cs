using System.Windows;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Tray;
using OptiGame.App.ViewModels;
using OptiGame.App.Views;
using OptiGame.Core;

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

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(AppPaths.Default);

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
