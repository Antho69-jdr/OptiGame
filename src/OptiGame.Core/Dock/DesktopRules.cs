namespace OptiGame.Core.Dock;

/// <summary>Fenêtre située au-dessus du bureau dans la pile des fenêtres (relevé Win32).</summary>
public sealed record WindowAbove(bool Visible, bool Cloaked, bool Minimized, bool Topmost, bool ToolWindow, bool HasArea);

/// <summary>
/// « Afficher le bureau » (Win+D) fait passer le bureau devant les applications : le dock collé au bureau serait caché.
/// Même méthode que Rainmeter : tant que le bureau est affiché, le dock passe devant, puis se recolle au bureau.
/// </summary>
public static class DesktopRules
{
    /// <summary>
    /// Fenêtre d'application qui recouvre le bureau. Ne comptent pas : fenêtres invisibles, masquées par Windows
    /// (« cloaked » : applications modernes en arrière-plan, 9 sur la machine de dev), réduites, toujours au premier
    /// plan (barre des tâches), fenêtres outils (docks, dont celui d'OptiGame et RocketDock) et fenêtres sans surface.
    /// </summary>
    public static bool Covers(WindowAbove window) =>
        window.Visible && !window.Cloaked && !window.Minimized && !window.Topmost && !window.ToolWindow && window.HasArea;

    /// <summary>
    /// Bureau affiché : il est au premier plan et aucune application n'est au-dessus de lui. Un simple clic sur le fond
    /// d'écran entre deux fenêtres donne aussi le premier plan au bureau, mais des fenêtres restent au-dessus.
    /// </summary>
    public static bool IsDesktopShown(bool desktopIsForeground, IEnumerable<WindowAbove> above) =>
        desktopIsForeground && !above.Any(Covers);
}
