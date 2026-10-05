using OptiGame.Core.Settings;

namespace OptiGame.Core.Dock;

/// <summary>
/// Réglages qui concernent le dock. settings.json est réécrit pour bien d'autres raisons (place de la fenêtre, jeux Steam
/// connus…) : le dock ne se replace (et ne se replie) que si l'un de ceux-ci a changé.
/// </summary>
public sealed record DockAppearance(
    bool Enabled,
    DockEdge Edge,
    int IconSize,
    DockIconShape IconShape,
    double Opacity,
    bool AutoHide,
    double HideDelay,
    bool ShowOptiGame,
    bool ShowNames)
{
    public static DockAppearance From(AppSettings s) =>
        new(s.DockEnabled, s.DockEdge, s.DockIconSize, s.DockIconShape, s.DockOpacity, s.DockAutoHide, s.DockHideDelay, s.DockShowOptiGame, s.DockShowNames);
}
