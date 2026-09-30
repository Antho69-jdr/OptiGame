namespace OptiGame.Core.Dock;

/// <summary>
/// Grossissement des icônes du dock (effet « dock de macOS »). L'axe du dock est « étiré » autour de la souris :
/// la densité d'étirement vaut <c>maxScale</c> sous la souris et décroît en cosinus jusqu'à 1 à <c>range</c> de
/// distance. Chaque case prend la place de son intervalle étiré : le point sous la souris ne bouge pas, les cases
/// restent jointives (ni trou ni chevauchement) et tout varie continûment avec la souris — la disposition au repos,
/// elle, ne change jamais (pas de recentrage du dock, donc pas de sursaut).
/// </summary>
public static class DockMagnification
{
    /// <summary>Densité d'étirement à une distance donnée de la souris.</summary>
    public static double Scale(double distance, double range, double maxScale)
    {
        if (range <= 0 || maxScale <= 1) return 1;
        var d = Math.Abs(distance);
        if (d >= range) return 1;
        return 1 + (maxScale - 1) * (1 + Math.Cos(Math.PI * d / range)) / 2;
    }

    /// <summary>Longueur étirée entre la souris et un point situé à <paramref name="offset"/> d'elle (signée).</summary>
    public static double Stretch(double offset, double range, double maxScale)
    {
        if (range <= 0 || maxScale <= 1) return offset;
        var a = (maxScale - 1) / 2;
        var u = Math.Abs(offset);
        var inside = Math.Min(u, range);
        var stretched = inside + a * (inside + range / Math.PI * Math.Sin(Math.PI * inside / range)) + (u - inside);
        return Math.Sign(offset) * stretched;
    }

    /// <summary>Position à l'écran d'un point du dock au repos, la souris étant en <paramref name="mouse"/>.</summary>
    public static double Map(double position, double mouse, double range, double maxScale) =>
        mouse + Stretch(position - mouse, range, maxScale);

    /// <summary>
    /// Case au repos [<paramref name="start"/>, start + <paramref name="length"/>] → échelle à lui appliquer et
    /// déplacement de son centre.
    /// </summary>
    public static (double Scale, double Shift) Place(double start, double length, double mouse, double range, double maxScale)
    {
        if (length <= 0) return (1, 0);
        var a = Map(start, mouse, range, maxScale);
        var b = Map(start + length, mouse, range, maxScale);
        return ((b - a) / length, (a + b) / 2 - (start + length / 2));
    }
}
