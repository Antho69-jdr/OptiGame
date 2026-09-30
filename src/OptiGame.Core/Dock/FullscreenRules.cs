namespace OptiGame.Core.Dock;

/// <summary>Rectangle écran en pixels (bords gauche/haut inclus, droit/bas exclus).</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>État de la fenêtre au premier plan, relevé par Platform (Win32).</summary>
public sealed record ForegroundWindowInfo(
    bool IsShell,
    bool OnDockMonitor,
    bool IsMaximized,
    bool HasCaption,
    ScreenRect Window,
    ScreenRect Monitor);

/// <summary>
/// Faut-il masquer le dock parce qu'une application occupe l'écran ? Règles établies après un faux positif réel
/// (2026-09-30) : Discord AGRANDI sur le second écran (sans barre des tâches) couvrait tout cet écran, et Windows
/// renvoyait même QUNS_BUSY. Une fenêtre agrandie n'est pas du plein écran, et seul l'écran du dock compte.
/// </summary>
public static class FullscreenRules
{
    // QUERY_USER_NOTIFICATION_STATE : 2 = BUSY (peu fiable, voir ci-dessus), 3 = Direct3D plein écran, 4 = présentation.
    public const int QunsRunningD3dFullScreen = 3;
    public const int QunsPresentationMode = 4;

    public static bool ShouldHideDock(int notificationState, ForegroundWindowInfo? foreground)
    {
        // Signaux sûrs : jeu en plein écran exclusif, mode présentation.
        if (notificationState is QunsRunningD3dFullScreen or QunsPresentationMode) return true;

        if (foreground is null || foreground.IsShell || !foreground.OnDockMonitor) return false;

        // Plein écran « fenêtré » (jeux sans bordure, vidéo) : fenêtre sans barre de titre, non agrandie,
        // qui couvre tout l'écran.
        var w = foreground.Window;
        var m = foreground.Monitor;
        var coversMonitor = w.Left <= m.Left && w.Top <= m.Top && w.Right >= m.Right && w.Bottom >= m.Bottom;
        return coversMonitor && !foreground.IsMaximized && !foreground.HasCaption;
    }
}
