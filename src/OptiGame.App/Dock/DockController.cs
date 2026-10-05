using System.Windows;
using OptiGame.App.ViewModels;
using OptiGame.Core.Dock;
using OptiGame.Core.Logging;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Platform.Display;

namespace OptiGame.App.Dock;

/// <summary>
/// Crée ou ferme la fenêtre du dock selon les réglages, et la masque quand une application est en plein écran. Pendant une
/// partie, elle est FERMÉE (réglage « LightDuringGames », sinon masquée) puis recréée à la fin : ses jaquettes sont libérées.
/// Rien ne tourne quand le dock est désactivé ou fermé (pas même la surveillance du plein écran).
/// </summary>
public sealed class DockController(
    AppSettingsStore settings,
    DockViewModel dock,
    GameSessionManager sessions,
    FullscreenWatcher fullscreen,
    FileLog log) : IDisposable
{
    private DockWindow? _window;
    private bool _started;
    private DockAppearance? _applied;

    public void Start()
    {
        if (_started) return;
        _started = true;
        settings.Changed += (_, _) => OnUi(OnSettingsChanged);
        sessions.SessionStarted += (_, _) => OnUi(UpdateSuppression);
        sessions.SessionEnded += (_, _) => OnUi(Apply); // fermé pour la partie : recréé selon les réglages
        fullscreen.FullscreenChanged += (_, _) => UpdateSuppression();
        fullscreen.DesktopShownChanged += (_, shown) =>
        {
            log.Info(shown ? "Bureau affiché (Win+D) : dock au premier plan." : "Bureau recouvert : dock recollé au bureau.");
            _window?.SetDesktopShown(shown);
        };
        Apply();
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
        fullscreen.Dispose();
    }

    /// <summary>
    /// settings.json est réécrit pour bien d'autres raisons (place de la fenêtre, jeux Steam connus…) : le dock ne se replace
    /// que si l'un de SES réglages a changé, et se montre alors 2 s pour qu'on voie l'effet (même s'il se masque seul).
    /// </summary>
    private void OnSettingsChanged()
    {
        var appearance = DockAppearance.From(settings.Get());
        if (appearance == _applied) return;
        Apply();
        _window?.Preview();
    }

    private void Apply()
    {
        var s = settings.Get();
        _applied = DockAppearance.From(s);
        if (!s.DockEnabled)
        {
            if (_window is not null)
            {
                CloseWindow();
                log.Info("Dock désactivé.");
            }
            return;
        }

        if (_window is null)
        {
            if (ClosedForGame) return; // recréé à la fin de la partie
            fullscreen.Start();
            var main = Application.Current?.MainWindow;
            _window = new DockWindow(dock, fullscreen);
            _window.Show();
            // WPF fait de la première fenêtre ouverte la fenêtre principale : le dock (sans focus, toujours là) ne doit
            // jamais servir de propriétaire aux dialogues.
            if (Application.Current is { } app && ReferenceEquals(app.MainWindow, _window)) app.MainWindow = main;
            dock.RefreshInstallStates();
            log.Info("Dock activé.");
        }
        _window.ApplySettings(s);
        _window.SetDesktopShown(fullscreen.IsDesktopShown);
        UpdateSuppression();
    }

    private bool ClosedForGame => sessions.Current is not null && settings.Get().LightDuringGames;

    /// <summary>
    /// Pendant une partie : fermé (ou masqué si l'allègement est désactivé). Masqué devant une application plein écran : il ne
    /// doit ni gêner, ni coûter des FPS. Ne crée JAMAIS la fenêtre : fullscreen.Start() (dans Apply) peut déclencher
    /// FullscreenChanged avant que _window soit posé, et Apply en cours créerait alors un deuxième dock.
    /// </summary>
    private void UpdateSuppression()
    {
        if (_window is null) return;
        if (ClosedForGame)
        {
            CloseWindow();
            log.Info("Dock fermé pendant la partie.");
            return;
        }
        _window.SetSuppressed(sessions.Current is not null || fullscreen.IsFullscreen);
    }

    private void CloseWindow()
    {
        if (_window is null) return;
        // Application.MainWindow (propriétaire des dialogues) ne doit pas garder en vie la fenêtre fermée.
        if (ReferenceEquals(Application.Current?.MainWindow, _window)) Application.Current!.MainWindow = null;
        _window.Close();
        _window = null;
        fullscreen.Dispose();
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
