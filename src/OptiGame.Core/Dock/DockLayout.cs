using OptiGame.Core.Settings;

namespace OptiGame.Core.Dock;

/// <summary>Dimensions des icônes du dock à partir des réglages (valeurs hors bornes ramenées dans les bornes).</summary>
public static class DockLayout
{
    public const int MinIconSize = 32;
    public const int MaxIconSize = 128;
    public const double MinOpacity = 0.2;

    /// <summary>Rapport hauteur / largeur d'une jaquette IGDB (264 × 374 ≈ 2:3).</summary>
    public const double CoverAspect = 1.5;

    public static (double Width, double Height) IconSize(int size, DockIconShape shape)
    {
        var width = (double)Math.Clamp(size, MinIconSize, MaxIconSize);
        return (width, shape == DockIconShape.Cover ? Math.Round(width * CoverAspect) : width);
    }

    public static double Opacity(double opacity) => double.IsFinite(opacity) ? Math.Clamp(opacity, MinOpacity, 1) : 0.9;
}
