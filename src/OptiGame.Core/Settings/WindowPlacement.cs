namespace OptiGame.Core.Settings;

/// <summary>Place de la fenêtre principale, gardée dans settings.json (unités WPF, hors état agrandi).</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>Fenêtre agrandie (plein écran de travail) à sa fermeture ; les dimensions ci-dessus sont celles d'avant.</summary>
    public bool Maximized { get; set; }
}

/// <summary>Rectangle d'écran en unités WPF (Core ne dépend pas de WPF).</summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// Taille et place de la fenêtre principale à son ouverture : la place gardée si sa barre de titre est encore visible
/// (écran débranché, résolution changée : non), sinon une fenêtre de 1240 × 860 au plus, jamais plus de 90 % de la zone de
/// travail (à 125-150 % d'échelle, 1240 × 860 dépasse l'écran), centrée.
/// </summary>
public static class WindowLayout
{
    public const double PreferredWidth = 1240;
    public const double PreferredHeight = 860;

    /// <summary>Largeur et hauteur visibles minimales de la barre de titre pour garder une place enregistrée.</summary>
    private const double MinVisibleWidth = 120;
    private const double TitleBarHeight = 32;

    public static ScreenRect InitialBounds(WindowPlacement? saved, ScreenRect workArea, ScreenRect virtualScreen, double minWidth, double minHeight)
    {
        if (saved is { Width: > 0, Height: > 0 } && TitleBarVisible(saved, virtualScreen))
        {
            return new ScreenRect(saved.Left, saved.Top,
                Math.Clamp(saved.Width, minWidth, Math.Max(minWidth, virtualScreen.Width)),
                Math.Clamp(saved.Height, minHeight, Math.Max(minHeight, virtualScreen.Height)));
        }

        var width = Math.Max(minWidth, Math.Min(PreferredWidth, workArea.Width * 0.9));
        var height = Math.Max(minHeight, Math.Min(PreferredHeight, workArea.Height * 0.9));
        return new ScreenRect(workArea.Left + (workArea.Width - width) / 2, workArea.Top + (workArea.Height - height) / 2, width, height);
    }

    private static bool TitleBarVisible(WindowPlacement saved, ScreenRect screen)
    {
        var visibleWidth = Math.Min(saved.Left + saved.Width, screen.Right) - Math.Max(saved.Left, screen.Left);
        var visibleHeight = Math.Min(saved.Top + TitleBarHeight, screen.Bottom) - Math.Max(saved.Top, screen.Top);
        return visibleWidth >= MinVisibleWidth && visibleHeight >= TitleBarHeight / 2;
    }
}
