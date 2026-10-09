using System.Windows;
using System.Windows.Media;
using OptiGame.Core.Settings;

namespace OptiGame.App.Services;

/// <summary>
/// Couleur d'accent choisie dans Paramètres › Général, appliquée tout de suite. Les pinceaux d'accent du thème (Brush.Accent,
/// AccentHover, AccentPressed, OnAccent, Selection, AccentSoft) sont lus en DynamicResource : ceux posés ici, au niveau de
/// l'application, priment sur ceux de Themes/Theme.xaml (que WPF fige au chargement : impossible de les recolorer, et un style
/// qui les lirait en StaticResource garderait le vert). Les clés d'accent du thème Fluent, posées directement dans App.xaml, sont
/// remplacées au même endroit. Revenir au vert retire les remplacements. Le logo garde la marque (Brush.Brand).
/// </summary>
public static class AccentAppearance
{
    private static readonly string[] ThemeBrushes = ["Brush.Accent", "Brush.AccentHover", "Brush.AccentPressed", "Brush.OnAccent", "Brush.Selection", "Brush.AccentSoft"];

    /// <summary>Valeurs d'origine des clés Fluent d'App.xaml, gardées au premier changement (pour revenir au vert).</summary>
    private static Dictionary<object, object>? _originals;

    private static AccentColor _applied = AccentColor.Green;

    public static void Attach(Application app, AppSettingsStore settings)
    {
        Apply(app, settings.Get().AccentColor);
        settings.Changed += (_, _) => app.Dispatcher.BeginInvoke(() => Apply(app, settings.Get().AccentColor));
    }

    public static void Apply(Application app, AccentColor color)
    {
        if (color == _applied) return;
        _applied = color;
        var r = app.Resources;
        var p = AccentColors.For(color);

        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        static SolidColorBrush B(string hex, double opacity = 1)
        {
            var brush = new SolidColorBrush(C(hex)) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        var fluent = new Dictionary<object, object>
        {
            ["AccentColor"] = C(p.Accent),
            ["AccentFillColorDefaultBrush"] = B(p.Accent),
            ["AccentFillColorSecondaryBrush"] = B(p.Accent, 0.9),
            ["AccentFillColorTertiaryBrush"] = B(p.Accent, 0.8),
            ["AccentFillColorSelectedTextBackgroundBrush"] = B(p.Accent),
            ["AccentDefaultBrush"] = B(p.Accent),
            ["AccentSecondaryBrush"] = B(p.Accent, 0.9),
            ["AccentTertiaryBrush"] = B(p.Accent, 0.8),
            ["AccentButtonBorderBrush"] = B(p.Pressed),
            ["AccentControlElevationBorderBrush"] = B(p.Pressed),
            ["AccentTextFillColorPrimaryBrush"] = B(p.Text),
            ["AccentTextFillColorSecondaryBrush"] = B(p.Light3),
            ["AccentTextFillColorTertiaryBrush"] = B(p.Hover),
            ["TextOnAccentFillColorPrimaryBrush"] = B(p.OnAccent),
            ["TextOnAccentFillColorSecondaryBrush"] = B(p.OnAccentSecondary),
            ["TextOnAccentFillColorSelectedTextBrush"] = B(p.OnAccent),
            [SystemColors.AccentColorKey] = C(p.Accent),
            [SystemColors.AccentColorLight1Key] = C(p.Hover),
            [SystemColors.AccentColorLight2Key] = C(p.Accent),
            [SystemColors.AccentColorLight3Key] = C(p.Light3),
            [SystemColors.AccentColorDark1Key] = C(p.Pressed),
            [SystemColors.AccentColorDark2Key] = C(p.Dark2),
            [SystemColors.AccentColorDark3Key] = C(p.Dark3),
            [SystemColors.AccentColorBrushKey] = B(p.Accent),
            [SystemColors.AccentColorLight1BrushKey] = B(p.Hover),
            [SystemColors.AccentColorLight2BrushKey] = B(p.Accent),
            [SystemColors.AccentColorLight3BrushKey] = B(p.Light3),
            [SystemColors.AccentColorDark1BrushKey] = B(p.Pressed),
            [SystemColors.AccentColorDark2BrushKey] = B(p.Dark2),
            [SystemColors.AccentColorDark3BrushKey] = B(p.Dark3),
        };
        _originals ??= fluent.Keys.Where(r.Contains).ToDictionary(k => k, k => r[k]);

        if (color == AccentColor.Green)
        {
            // Retour au thème : remplacements retirés, valeurs d'origine d'App.xaml remises.
            foreach (var key in ThemeBrushes) r.Remove(key);
            foreach (var (key, value) in _originals) r[key] = value;
            return;
        }

        r["Brush.Accent"] = B(p.Accent);
        r["Brush.AccentHover"] = B(p.Hover);
        r["Brush.AccentPressed"] = B(p.Pressed);
        r["Brush.OnAccent"] = B(p.OnAccent);
        r["Brush.Selection"] = B(p.Accent);
        r["Brush.AccentSoft"] = B(p.Accent, 0.12);
        foreach (var (key, value) in fluent) r[key] = value;
    }
}
