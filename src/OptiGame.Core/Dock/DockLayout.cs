using OptiGame.Core.Settings;

namespace OptiGame.Core.Dock;

/// <summary>Dimensions des icônes du dock à partir des réglages (valeurs hors bornes ramenées dans les bornes).</summary>
public static class DockLayout
{
    public const int MinIconSize = 32;
    public const int MaxIconSize = 128;
    public const double MinOpacity = 0;

    /// <summary>Rapport hauteur / largeur d'une jaquette IGDB : 264 × 352 (3:4), mesuré sur les fichiers en cache.</summary>
    public const double CoverAspect = 4.0 / 3.0;

    public static (double Width, double Height) IconSize(int size, DockIconShape shape)
    {
        var width = (double)Math.Clamp(size, MinIconSize, MaxIconSize);
        return (width, shape == DockIconShape.Cover ? Math.Round(width * CoverAspect) : width);
    }

    public static double Opacity(double opacity) => double.IsFinite(opacity) ? Math.Clamp(opacity, MinOpacity, 1) : 0.9;

    /// <summary>
    /// Alpha (0-255) du fond et de la bordure du plateau. Jamais 0 : sur une fenêtre transparente, Windows fait
    /// traverser les clics aux pixels d'alpha nul ; le dock perdrait la souris entre deux icônes et se replierait.
    /// 1/255 est invisible à l'œil mais garde le dock réactif.
    /// </summary>
    public static (byte Background, byte Border) PlateAlpha(double opacity)
    {
        var o = Opacity(opacity);
        return ((byte)Math.Max(1, Math.Round(o * 255)), (byte)Math.Round(o * 0x40));
    }
}
