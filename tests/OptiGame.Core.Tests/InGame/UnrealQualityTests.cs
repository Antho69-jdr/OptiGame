using OptiGame.Core.InGame;
using OptiGame.Core.Rating;
using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.InGame;

public sealed class IniTextTests
{
    private const string Ini = "[/Script/Engine.GameUserSettings]\r\nbUseVSync=False\r\n\r\n[ScalabilityGroups]\r\nsg.ResolutionQuality=100.000000\r\nsg.ShadowQuality=2\r\n\r\n[Other]\r\nX=1\r\n";

    [Fact]
    public void Reads_a_value_of_a_section()
    {
        Assert.Equal("2", IniText.Get(Ini, "ScalabilityGroups", "sg.ShadowQuality"));
        Assert.Equal("2", IniText.Get(Ini, "scalabilitygroups", "SG.SHADOWQUALITY")); // casse ignorée, comme Unreal
        Assert.Null(IniText.Get(Ini, "ScalabilityGroups", "sg.TextureQuality"));
        Assert.Null(IniText.Get(Ini, "Absente", "X"));
        Assert.Null(IniText.Get(Ini, "ScalabilityGroups", "X")); // X est dans une autre section
    }

    [Fact]
    public void Changing_a_value_keeps_everything_else()
    {
        var updated = IniText.Set(Ini, "ScalabilityGroups", "sg.ShadowQuality", "3");
        Assert.Equal(Ini.Replace("sg.ShadowQuality=2", "sg.ShadowQuality=3"), updated);
        Assert.Equal(Ini, IniText.Set(updated, "ScalabilityGroups", "sg.ShadowQuality", "2"));
    }

    [Fact]
    public void Adds_and_removes_keys_and_sections()
    {
        var added = IniText.Set(Ini, "ScalabilityGroups", "sg.TextureQuality", "1");
        Assert.Contains("sg.ShadowQuality=2\r\nsg.TextureQuality=1\r\n\r\n[Other]", added); // fin de section, avant la ligne vide
        Assert.Equal(Ini, IniText.Set(added, "ScalabilityGroups", "sg.TextureQuality", null)); // retirée = texte d'origine

        var newSection = IniText.Set("A=1\n", "ScalabilityGroups", "sg.ShadowQuality", "1");
        Assert.Equal("A=1\n\n[ScalabilityGroups]\nsg.ShadowQuality=1\n", newSection);
        Assert.Equal(Ini, IniText.Set(Ini, "Absente", "X", null));
    }
}

public sealed class UnrealQualityTests
{
    private static readonly UnrealSettings.QualityScale Pubg = UnrealSettings.ScaleOf("TslGame");

    [Theory]
    [InlineData(GraphicsPreset.Low, 1)]     // « Bas », pas « Très bas »
    [InlineData(GraphicsPreset.Medium, 2)]
    [InlineData(GraphicsPreset.High, 3)]
    [InlineData(GraphicsPreset.Ultra, 4)]
    public void Picks_the_level_of_pubg(GraphicsPreset preset, int level) => Assert.Equal(level, UnrealQuality.LevelFor(Pubg, preset));

    [Fact]
    public void Ultra_of_the_engine_is_epic_not_cinematic() =>
        Assert.Equal(3, UnrealQuality.LevelFor(UnrealSettings.EngineScale, GraphicsPreset.Ultra));

    [Fact]
    public void Change_writes_every_quality_group_of_the_file()
    {
        var ini = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InGame", "Samples", "pubg-GameUserSettings-excerpt.ini"));
        var settings = UnrealSettings.Parse(ini, @"C:\Users\x\AppData\Local\TslGame\Saved\Config\WindowsNoEditor\GameUserSettings.ini",
            DateTime.Now, "TslGame")!;
        var id = Guid.NewGuid();

        var change = UnrealQuality.Change(id, "PUBG", settings, 3);

        Assert.Equal($"game.unreal-quality.{id:N}", change.Id);
        Assert.Equal("PUBG : qualité graphique « Élevé »", change.Title);
        Assert.Contains("« Moyen » (niveau 2) → « Élevé » (niveau 3)", change.What);
        Assert.Equal(7, change.Writes.Count); // sans sg.ResolutionQuality
        Assert.All(change.Writes, w => Assert.Equal(("3", KnownSettings.IniValueKind), (w.NewValue.Text, w.Target.Kind)));
        Assert.Contains(change.Writes, w => w.Target == KnownSettings.IniValue(settings.SourcePath, "ScalabilityGroups", "sg.ShadowQuality"));
        Assert.False(change.RequiresAdmin);
    }
}
