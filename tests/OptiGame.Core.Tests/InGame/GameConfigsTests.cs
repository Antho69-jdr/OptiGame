using OptiGame.Core.InGame;
using OptiGame.Core.Rating;

namespace OptiGame.Core.Tests.InGame;

/// <summary>Base des réglages des jeux (Definitions/games.json) et fichiers réels relevés sur la machine de dev.</summary>
public sealed class GameConfigsTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InGame", "Samples", name));

    private static readonly string[] Roles = ["preset", "width", "height", "displayMode", "vsync", "frameLimit", "upscaler", "upscalerMode"];

    [Fact]
    public void Every_definition_is_well_formed()
    {
        Assert.NotEmpty(GameConfigs.All);
        Assert.Equal(GameConfigs.All.Count, GameConfigs.All.Select(d => d.Id).Distinct().Count());
        foreach (var game in GameConfigs.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(game.Verified)); // chaque jeu dit comment son sens a été vérifié
            Assert.EndsWith(".exe", game.Exe, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("{", game.Path);
            Assert.Equal(game.Settings.Count, game.Settings.Select(s => s.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var setting in game.Settings)
            {
                Assert.True(setting.Role is null || Roles.Contains(setting.Role), $"{game.Id}.{setting.Key} : rôle {setting.Role}");
                Assert.True(setting.Label is not null || setting.Role is not null, $"{game.Id}.{setting.Key} : ni libellé ni rôle");
                Assert.All(setting.Presets?.Values ?? [], p => Assert.True(Enum.TryParse<GraphicsPreset>(p, out _), p));
                Assert.All(setting.DisplayModes?.Values ?? [], m => Assert.True(Enum.TryParse<InGameDisplayMode>(m, out _), m));
                Assert.All(setting.When?.Keys ?? [], k => Assert.Contains(game.Settings, s => s.Key == k));
            }
        }
    }

    [Fact]
    public void Finds_a_game_by_its_exe_name()
    {
        Assert.Equal("void-crew", GameConfigs.For(@"A:\SteamLibrary\steamapps\common\Void Crew\Void Crew.exe")?.Id);
        Assert.Null(GameConfigs.For(@"C:\Jeux\Inconnu\jeu.exe"));
    }

    [Fact]
    public void Void_crew_settings_in_the_game_words()
    {
        var definition = GameConfigs.For("Void Crew.exe")!;
        var values = GameConfigs.ReadValues(definition.Format, Sample("voidcrew-Settings.json"));
        var settings = GameConfigs.Interpret(definition, values, "Settings.json", "Settings.json", new DateTime(2026, 10, 4, 21, 41, 0))!;

        // Préréglage 3 = « Personnalisé » (liste du menu : Bas, Moyen, Élevé, Personnalisé) : pas de réglage d'OptiGame déduit.
        Assert.Null(settings.Preset);
        Assert.Equal((3440, 1440), (settings.Width, settings.Height));
        Assert.Equal(InGameDisplayMode.Borderless, settings.DisplayMode);
        Assert.False(settings.VSync); // VSync = 2 : limite fixe…
        Assert.Equal(120, settings.FrameLimit); // … à TargetFramerate = 120
        Assert.Equal("DLSS Équilibré", settings.Upscaler);
        Assert.Equal(
            [
                new InGameOption("Préréglage", "Personnalisé"), new InGameOption("Mode d'affichage", "Plein écran fenêtré"),
                new InGameOption("Limite d'images", "Limite fixe"), new InGameOption("Limite de FPS", "120 FPS"),
                new InGameOption("Upscaling", "DLSS"), new InGameOption("Mode de l'upscaling", "Équilibré"),
                new InGameOption("Anticrénelage", "SMAA"), new InGameOption("Textures", "Élevé"), new InGameOption("Détail des modèles", "Élevé"),
                new InGameOption("Ombres", "Élevé"), new InGameOption("Rendu volumétrique", "Élevé"), new InGameOption("Halo lumineux (bloom)", "Élevé"),
            ],
            settings.Options);
        Assert.Equal("3440×1440, plein écran fenêtré, V-Sync désactivée, limite de 120 FPS, DLSS Équilibré", settings.Summary());
    }

    [Fact]
    public void Star_citizen_settings_from_its_attributes_file()
    {
        var definition = GameConfigs.For(@"A:\Jeux\Roberts Space Industries\StarCitizen\LIVE\Bin64\StarCitizen.exe")!;
        Assert.Equal("xml-attributes", definition.Format);
        var values = GameConfigs.ReadValues(definition.Format, Sample("starcitizen-attributes.xml"));
        var settings = GameConfigs.Interpret(definition, values, "attributes.xml", "attributes.xml", new DateTime(2026, 9, 29, 23, 19, 0))!;

        Assert.Equal(GraphicsPreset.Low, settings.Preset); // sys_spec 1 = low (aide de StarCitizen.exe)
        Assert.Equal((3440, 1440), (settings.Width, settings.Height));
        Assert.Equal(InGameDisplayMode.Fullscreen, settings.DisplayMode); // r_WindowMode 2 = fullscreen
        Assert.Null(settings.VSync); // absente du fichier : jamais supposée
        Assert.Contains(new InGameOption("Upscaling", "Qualité"), settings.Options!);
        Assert.Contains(new InGameOption("Modèle DLSS", "Transformer (préréglage K)"), settings.Options!);
        Assert.Contains(new InGameOption("Distance d'affichage des objets", "Élevé"), settings.Options!);
        Assert.Contains(new InGameOption("Filtrage des textures", "Moyen"), settings.Options!);
        Assert.Contains(new InGameOption("Flou de mouvement", "Désactivé"), settings.Options!);
        Assert.DoesNotContain(settings.Options!, o => o.Value.Contains("inconnue")); // toutes les valeurs du vrai fichier sont connues
    }

    [Fact]
    public void Xml_attributes_without_any_attr_is_refused()
    {
        Assert.Throws<FormatException>(() => GameConfigs.ReadValues("xml-attributes", "<Attributes Version=\"35\"/>"));
        Assert.Throws<FormatException>(() => GameConfigs.ReadValues("xml-attributes", "{\"a\":1}"));
    }

    [Fact]
    public void Unknown_values_are_shown_never_guessed_and_conditions_apply()
    {
        var definition = GameConfigs.For("Void Crew.exe")!;
        var values = new Dictionary<string, string> { ["VSync"] = "1", ["TargetFramerate"] = "120", ["ShadowQuality"] = "7", ["UpscaleKind"] = "NONE", ["UpscaleQuality"] = "1" };
        var settings = GameConfigs.Interpret(definition, values, "Settings.json", "Settings.json", DateTime.Now)!;

        Assert.Equal(0, settings.FrameLimit); // VSync = 1 : sans limite (la limite enregistrée ne sert pas)
        Assert.DoesNotContain(settings.Options!, o => o.Label == "Limite de FPS");
        Assert.Contains(new InGameOption("Ombres", "valeur 7, inconnue"), settings.Options!);
        Assert.Null(settings.Upscaler); // NONE = aucun ; le mode d'upscaling ne s'applique qu'au DLSS
        Assert.DoesNotContain(settings.Options!, o => o.Label == "Mode de l'upscaling");
    }

    [Fact]
    public void A_file_in_another_format_is_refused()
    {
        Assert.Throws<FormatException>(() => GameConfigs.ReadValues("json", "[sg]\nsg.ShadowQuality=2"));
        Assert.Throws<FormatException>(() => GameConfigs.ReadValues("xml", "<a/>"));
    }
}
