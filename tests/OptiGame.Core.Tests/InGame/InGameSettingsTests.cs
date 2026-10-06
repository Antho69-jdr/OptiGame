using OptiGame.Core.InGame;
using OptiGame.Core.Rating;

namespace OptiGame.Core.Tests.InGame;

public sealed class InGameSettingsTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InGame", "Samples", name));

    private static readonly DateTime Saved = new(2026, 10, 5, 23, 31, 0);

    [Fact]
    public void Reads_the_real_settings_of_arc_raiders_unreal_engine_5()
    {
        var settings = UnrealSettings.Parse(Sample("arc-raiders-GameUserSettings.ini"), "GameUserSettings.ini", Saved)!;

        Assert.Equal("Unreal Engine", settings.Engine);
        Assert.Equal(GraphicsPreset.Medium, settings.Preset); // tous les groupes à 1 ; le paysage (3) est ignoré
        Assert.Equal("10 groupes de qualité au niveau 1 (du moteur, 0 à 4)", settings.PresetDetail);
        Assert.Equal((3440, 1440), (settings.Width, settings.Height));
        Assert.Equal(87, settings.RenderScalePercent);
        Assert.Equal(InGameDisplayMode.Borderless, settings.DisplayMode);
        Assert.Equal((true, 0), (settings.VSync, settings.FrameLimit));
        Assert.Equal("DLSS Qualité", settings.Upscaler);
        Assert.Equal("qualité Moyen, 3440×1440, plein écran fenêtré, V-Sync activée, sans limite de FPS, DLSS Qualité",
            settings.Summary());
    }

    [Fact]
    public void Reads_the_real_settings_of_pubg_unreal_engine_4()
    {
        var settings = UnrealSettings.Parse(Sample("pubg-GameUserSettings-excerpt.ini"), "GameUserSettings.ini", Saved, "TslGame")!;

        // Niveau 2 pour les 7 groupes = « Moyen » dans PUBG (Très bas … Ultra, confirmé par l'utilisateur), « Élevé » du moteur.
        Assert.Equal(GraphicsPreset.Medium, settings.Preset);
        Assert.Equal("7 groupes de qualité au niveau 2 (« Moyen » dans PUBG)", settings.PresetDetail);
        Assert.Equal(GraphicsPreset.High, UnrealSettings.Parse(Sample("pubg-GameUserSettings-excerpt.ini"), "x", Saved)!.Preset);
        Assert.Equal((3440, 1440), (settings.Width, settings.Height)); // et non LastUserConfirmed… (1920×1080)
        Assert.Null(settings.RenderScalePercent);  // 100 %
        Assert.Equal((false, 0), (settings.VSync, settings.FrameLimit)); // FrameRateLimit=1000 = sans limite
        Assert.Null(settings.Upscaler);            // UpscalingMethod=None
    }

    [Fact]
    public void Mixed_levels_use_the_median_and_say_so()
    {
        const string ini = "[ScalabilityGroups]\nsg.ViewDistanceQuality=3\nsg.ShadowQuality=0\nsg.TextureQuality=3\nsg.EffectsQuality=2\nsg.FoliageQuality=1\n";
        var settings = UnrealSettings.Parse(ini, "x", Saved)!;

        Assert.Equal(GraphicsPreset.High, settings.Preset);
        Assert.Equal("5 groupes de qualité, niveaux 0 à 3, médiane 2 (du moteur, 0 à 4)", settings.PresetDetail);
    }

    [Fact]
    public void Nothing_readable_gives_nothing()
    {
        Assert.Null(UnrealSettings.Parse("[Internationalization]\nCulture=fr\n", "x", Saved));
        Assert.Null(UnrealSettings.Parse("[ScalabilityGroups]\nsg.ShadowQuality=2\n", "x", Saved)); // trop peu de groupes pour un réglage
    }

    [Fact]
    public void Reads_the_screen_settings_of_a_unity_game_void_crew()
    {
        Assert.Equal(("Hutlihut Games ApS", "Void Crew"), UnitySettings.ParseAppInfo("Hutlihut Games ApS\nVoid Crew"));
        Assert.Null(UnitySettings.ParseAppInfo("Seulement une ligne"));

        // Valeurs réelles du registre (2026-10-06).
        var values = new Dictionary<string, int>
        {
            ["Screenmanager Resolution Width_h182942802"] = 3440,
            ["Screenmanager Resolution Height_h2627697771"] = 1440,
            ["Screenmanager Fullscreen mode_h3630240806"] = 1,
            ["Screenmanager Resolution Width Default_h680557497"] = 1920,
            ["Screenmanager Resolution Window Width_h2524650974"] = 1600,
            ["Screenmanager Fullscreen mode Default_h401710285"] = 3,
        };
        var settings = UnitySettings.FromRegistry(values, @"HKCU\Software\Hutlihut Games ApS\Void Crew", Saved)!;

        Assert.Equal(("Unity", null), (settings.Engine, settings.Preset)); // le niveau de qualité Unity n'est pas lu
        Assert.Equal((3440, 1440, InGameDisplayMode.Borderless), (settings.Width, settings.Height, settings.DisplayMode));
        Assert.Equal("Lu dans le registre de Windows (Unity), enregistré hier à 23:31 : 3440×1440, plein écran fenêtré.",
            settings.Description(new DateTime(2026, 10, 6, 20, 0, 0)));
        Assert.Null(UnitySettings.FromRegistry(new Dictionary<string, int>(), "x", Saved));
    }
}
