using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.Settings;

/// <summary>Couleurs d'accent : chacune garde des contrastes lisibles (WCAG 2.x) sur les surfaces réelles du thème.</summary>
public sealed class AccentColorsTests
{
    public static TheoryData<AccentColor> All => [.. Enum.GetValues<AccentColor>()];

    [Theory]
    [MemberData(nameof(All))]
    public void Every_accent_stays_readable(AccentColor color)
    {
        var p = AccentColors.For(color);
        Assert.True(AccentColors.Contrast(p.OnAccent, p.Accent) >= 4.5, $"{color} : texte sur le bouton principal");
        Assert.True(AccentColors.Contrast(p.OnAccent, p.Hover) >= 4.5, $"{color} : texte sur le bouton survolé");
        Assert.True(AccentColors.Contrast(p.OnAccent, p.Pressed) >= 4.5, $"{color} : texte sur le bouton appuyé");
        Assert.True(AccentColors.Contrast(p.Accent, AccentColors.Window) >= 3, $"{color} : trait de sélection sur la fenêtre");
        Assert.True(AccentColors.Contrast(p.Accent, AccentColors.Card) >= 3, $"{color} : case cochée sur une carte");
        Assert.True(AccentColors.Contrast(p.Text, AccentColors.Window) >= 4.5, $"{color} : texte d'accent sur la fenêtre");
    }

    [Fact]
    public void Green_keeps_the_exact_theme_values()
    {
        var green = AccentColors.For(AccentColor.Green);
        Assert.Equal(("#2FD27A", "#4BE391", "#22B566", "#06140C"), (green.Accent, green.Hover, green.Pressed, green.OnAccent));
        Assert.Equal(AccentColor.Green, new AppSettings().AccentColor);
    }

    [Fact]
    public void Accent_survives_cloning()
    {
        var settings = new AppSettings { AccentColor = AccentColor.Blue };
        Assert.Equal(AccentColor.Blue, settings.Clone().AccentColor);
    }

    [Fact]
    public void Mix_and_contrast_follow_the_definitions()
    {
        Assert.Equal("#808080", AccentColors.Mix("#000000", "#FFFFFF", 0.5019));
        Assert.Equal(21, AccentColors.Contrast("#000000", "#FFFFFF"), 1);
    }
}
