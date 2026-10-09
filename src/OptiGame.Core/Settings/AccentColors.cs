using System.Globalization;

namespace OptiGame.Core.Settings;

/// <summary>
/// Couleur d'accent d'OptiGame (Paramètres › Général) : boutons principaux, sélection, cases cochées. Le logo garde le vert de la
/// marque. Pas de jaune, d'orange ni de rouge : réservés aux statuts (avertissement, série « après » des graphes, erreur).
/// </summary>
public enum AccentColor
{
    Green,
    Teal,
    Blue,
    Violet,
}

/// <summary>Teintes d'un accent, au format « #RRGGBB » (clés Color.Accent* de Themes/Theme.xaml et clés d'accent Fluent).</summary>
public sealed record AccentPalette(string Accent, string Hover, string Pressed, string OnAccent, string OnAccentSecondary, string Text,
    string Light3, string Dark2, string Dark3);

public static class AccentColors
{
    /// <summary>Fond de la fenêtre et des cartes (Theme.xaml) : les contrastes sont vérifiés dessus (tests).</summary>
    public const string Window = "#0E1014";
    public const string Card = "#181C23";

    /// <summary>Le vert d'origine : valeurs EXACTES de Theme.xaml (rien ne change pour qui garde le vert).</summary>
    private static readonly AccentPalette Green = new("#2FD27A", "#4BE391", "#22B566", "#06140C", "#1A3A28", "#6FEBA8", "#98F2C1", "#198F50", "#11693A");

    public static AccentPalette For(AccentColor color) => color switch
    {
        AccentColor.Teal => Derive("#2CC7BE"),
        AccentColor.Blue => Derive("#5C9DFF"),
        AccentColor.Violet => Derive("#A98BFA"),
        _ => Green,
    };

    /// <summary>Teintes dérivées d'une couleur de base, comme celles du vert : plus claire au survol, plus sombre à l'appui, texte foncé dessus.</summary>
    private static AccentPalette Derive(string accent) => new(
        accent,
        Mix(accent, "#FFFFFF", 0.15),
        Mix(accent, "#000000", 0.14),
        Mix(accent, "#000000", 0.90),
        Mix(accent, "#000000", 0.72),
        Mix(accent, "#FFFFFF", 0.30),
        Mix(accent, "#FFFFFF", 0.45),
        Mix(accent, "#000000", 0.32),
        Mix(accent, "#000000", 0.50));

    /// <summary>Mélange de deux couleurs (0 = la première, 1 = la seconde).</summary>
    public static string Mix(string a, string b, double amount)
    {
        var (ra, ga, ba) = Rgb(a);
        var (rb, gb, bb) = Rgb(b);
        int Channel(int x, int y) => (int)Math.Round(x + (y - x) * amount);
        return $"#{Channel(ra, rb):X2}{Channel(ga, gb):X2}{Channel(ba, bb):X2}";
    }

    /// <summary>Contraste WCAG 2.x entre deux couleurs (1 à 21).</summary>
    public static double Contrast(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Linear(int channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    public static (int R, int G, int B) Rgb(string hex) =>
        (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber),
         int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));
}
