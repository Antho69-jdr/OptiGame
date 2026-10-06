using OptiGame.Core.InGame;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Platform.InGame;

namespace OptiGame.Platform.Tests;

/// <summary>Vrai fichier (copie temporaire d'un extrait réel du GameUserSettings.ini de PUBG) et vrai journal.</summary>
public sealed class IniFileAccessorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("OptiGame.Tests.").FullName;
    private readonly string _ini;
    private readonly IniFileAccessor _accessor = new();

    public IniFileAccessorTests()
    {
        _ini = Path.Combine(_dir, "GameUserSettings.ini");
        // Extrait réel (PUBG, 2026-10-06), CRLF comme le fichier d'origine.
        File.WriteAllText(_ini, string.Join("\r\n",
            "[/Script/TslGame.TslGameUserSettings]", "bUseVSync=False", "ResolutionSizeX=3440", "FrameRateLimit=1000.000000", "",
            "[ScalabilityGroups]", "sg.ResolutionQuality=100.000000", "sg.ViewDistanceQuality=2", "sg.AntiAliasingQuality=2",
            "sg.ShadowQuality=2", "sg.PostProcessQuality=2", "sg.TextureQuality=2", "sg.EffectsQuality=2", "sg.FoliageQuality=2", "",
            "[ShaderPipelineCache.CacheFile]", "LastOpened=TslGame", ""));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Reads_and_writes_one_value()
    {
        var shadow = KnownSettings.IniValue(_ini, "ScalabilityGroups", "sg.ShadowQuality");
        Assert.Equal("2", _accessor.Read(shadow).Text);
        Assert.True(_accessor.Read(KnownSettings.IniValue(_ini, "ScalabilityGroups", "sg.Nope")).IsAbsent);
        Assert.True(_accessor.Read(KnownSettings.IniValue(Path.Combine(_dir, "absent.ini"), "S", "K")).IsAbsent);

        _accessor.Write(shadow, SettingValue.String("3"));
        Assert.Equal("3", _accessor.Read(shadow).Text);
        Assert.False(File.Exists(_ini + ".optigame.tmp"));
        Assert.Throws<FileNotFoundException>(() => _accessor.Write(KnownSettings.IniValue(Path.Combine(_dir, "absent.ini"), "S", "K"), SettingValue.String("1")));
    }

    [Fact]
    public void Apply_then_undo_gives_back_the_exact_same_file()
    {
        var original = File.ReadAllBytes(_ini);
        var journal = new ChangeJournal(new JsonStateStore<JournalDocument>(Path.Combine(_dir, "fixes.json")), new SettingAccessors([_accessor]));
        var settings = UnrealSettings.Parse(File.ReadAllText(_ini), _ini, File.GetLastWriteTime(_ini), "TslGame")!;
        var change = UnrealQuality.Change(Guid.NewGuid(), "PUBG", settings, UnrealQuality.LevelFor(settings.Scale!, Core.Rating.GraphicsPreset.High));

        journal.Apply(change);
        var applied = UnrealSettings.Parse(File.ReadAllText(_ini), _ini, DateTime.Now, "TslGame")!;
        Assert.Equal((Core.Rating.GraphicsPreset.High, 3), (applied.Preset, applied.QualityLevel));
        Assert.Contains("sg.ResolutionQuality=100.000000", File.ReadAllText(_ini)); // échelle de rendu inchangée

        Assert.True(journal.Undo(change.Id).Success);
        Assert.Equal(original, File.ReadAllBytes(_ini));
    }
}
