namespace OptiGame.Core.Dock;

/// <summary>
/// Grossissement des icônes du dock (effet « dock de macOS ») : maximal sous la souris, décroissant en cosinus
/// jusqu'à 1 à <c>range</c> de distance. Lisse (pas de saut) et symétrique.
/// </summary>
public static class DockMagnification
{
    public static double Scale(double distance, double range, double maxScale)
    {
        if (range <= 0 || maxScale <= 1) return 1;
        var d = Math.Abs(distance);
        if (d >= range) return 1;
        return 1 + (maxScale - 1) * (1 + Math.Cos(Math.PI * d / range)) / 2;
    }
}
