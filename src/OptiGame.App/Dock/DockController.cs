using System.Windows;
using OptiGame.App.ViewModels;
using OptiGame.Core.Logging;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Platform.Display;

namespace OptiGame.App.Dock;

/// <summary>
/// Crée ou ferme la fenêtre du dock selon les réglages, et la masque pendant une session de jeu ou quand une
/// application est en plein écran. Rien ne tourne quand le dock est désactivé (pas même la surveillance du plein écran).
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

    public void Start()
    {
        if (_started) return;
        _started = true;
        settings.Changed += (_, _) => OnUi(Apply);
        sessions.SessionStarted += (_, _) => OnUi(UpdateSuppression);
        sessions.SessionEnded += (_, _) => OnUi(UpdateSuppression);
        fullscreen.FullscreenChanged += (_, _) => UpdateSuppression();
        Apply();
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
        fullscreen.Dispose();
    }

    private void Apply()
    {
        var s = settings.Get();
        if (!s.DockEnabled)
        {
            if (_window is not null)
            {
                _window.Close();
                _window = null;
                fullscreen.Dispose();
                log.Info("Dock désactivé.");
            }
            return;
        }

        if (_window is null)
        {
            fullscreen.Start();
            _window = new DockWindow(dock, fullscreen);
            _window.Show();
            log.Info("Dock activé.");
        }
        _window.ApplySettings(s.DockEdge, s.DockIconSize, s.DockIconShape, s.DockOpacity, s.DockAutoHide);
        UpdateSuppression();
    }

    /// <summary>Masqué pendant une partie ou une application plein écran : il ne doit ni gêner, ni coûter des FPS.</summary>
    private void UpdateSuppression() =>
        _window?.SetSuppressed(sessions.Current is not null || fullscreen.IsFullscreen);

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
