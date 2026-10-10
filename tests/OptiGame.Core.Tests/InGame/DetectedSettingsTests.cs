using OptiGame.Core.InGame;

namespace OptiGame.Core.Tests.InGame;

/// <summary>Détection automatique sur les fichiers réels des jeux sans définition de la machine de dev (2026-10-10).</summary>
public sealed class DetectedSettingsTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InGame", "Samples", name));

    private static InGameSettings? Detect(string text) => DetectedSettings.Interpret(DetectedSettings.Parse(text), "f", "f", DateTime.Now);

    [Fact]
    public void Overwatch_ini_gives_vsync_from_a_switch_named_enabled()
    {
        var settings = Detect(Sample("overwatch-Settings_v0.ini"))!;

        Assert.Equal(DetectedSettings.Engine, settings.Engine);
        Assert.True(settings.VSync); // VerticalSyncEnabled = "1"
        Assert.Null(settings.Width); // aucune dimension dans ce fichier : rien n'est supposé
        Assert.Null(settings.DisplayMode); // WindowMode = "1" : numérotation du jeu, inconnue
        Assert.Contains(settings.Options!, o => o.Label == "Render.13.WindowMode" && o.Value == "1"); // montré tel quel
        Assert.DoesNotContain(settings.Options!, o => o.Label.Contains("SoundQuality")); // le son n'est pas un réglage d'image
    }

    [Fact]
    public void Portal2_video_txt_gives_resolution_and_documented_vsync()
    {
        var settings = Detect(Sample("portal2-video.txt"))!;

        Assert.Equal((3440, 1440), (settings.Width, settings.Height)); // setting.defaultres / defaultresheight
        Assert.True(settings.VSync); // setting.mat_vsync 1 (variable documentée de Source)
        Assert.Null(settings.DisplayMode); // setting.fullscreen 0 : fenêtré OU sans bordure, non tranché
    }

    [Fact]
    public void Scrap_mechanic_json_gives_resolution_and_frame_cap_but_not_an_ambiguous_vsync()
    {
        var settings = Detect(Sample("scrapmechanic-settings.json"))!;

        Assert.Equal((3440, 1440), (settings.Width, settings.Height));
        Assert.Equal(165, settings.FrameLimit); // FrameRateCap 165.0
        Assert.Null(settings.VSync); // « VerticalSync : 1 » : le nom ne dit pas que c'est un interrupteur
        Assert.DoesNotContain(settings.Options!, o => o.Label.Contains("Volume"));
    }

    [Fact]
    public void Void_crew_vsync_enum_is_never_read_as_a_switch()
    {
        // Void Crew (définition vérifiée) : VSync 0 = V-Sync ACTIVÉE. Sans définition, ce 0 ne doit pas devenir « désactivée ».
        var settings = Detect(Sample("voidcrew-Settings.json"));

        Assert.Null(settings?.VSync);
    }

    [Fact]
    public void Video_settings_file_ranks_above_a_full_variable_dump()
    {
        var video = DetectedSettings.Parse(Sample("portal2-video.txt"));
        var dump = DetectedSettings.Parse(string.Join('\n', Enumerable.Range(0, 300).Select(i => $"cl_var{i} \"{i}\"")) +
                                          "\nmat_vsync \"1\"\nfov_desired \"90\"\ncl_minimal_rtt_shadows \"1\"\nr_lod \"0\"");

        Assert.True(DetectedSettings.Rank(video) > DetectedSettings.Rank(dump));
    }

    [Fact]
    public void Files_without_display_settings_are_ignored() =>
        Assert.Null(Detect("""{ "MasterVolume": 1.0, "Language": "French", "MouseSpeed": 0.7 }"""));

    [Theory]
    [InlineData("Resolution=2560x1440", 2560, 1440)]
    [InlineData("ScreenWidth = 1920\nScreenHeight = 1080\nShadowQuality = 2", 1920, 1080)]
    public void Common_resolution_forms_are_read(string text, int width, int height)
    {
        var settings = Detect(text + "\nTextureQuality=2\nVSync=true\nShadowQuality=1");

        Assert.Equal((width, height), (settings!.Width, settings.Height));
        Assert.True(settings.VSync); // « true » en toutes lettres : sans ambiguïté
    }
}
