using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Settings;

/// <summary>Auto HDR par jeu avec le vrai journal : plusieurs changements successifs, puis retour exact à l'origine.</summary>
public sealed class GameGraphicsJournalTests : IDisposable
{
    private const string Pubg = @"A:\SteamLibrary\steamapps\common\PUBG\TslGame\Binaries\Win64\TslGame.exe";
    private const string PubgDotted = @"A:\SteamLibrary\steamapps\common\PUBG\TslGame\Binaries\.\Win64\TslGame.exe";
    private const string WindowsValue = "AppStatus=1;AutoHDREnable=2097;"; // valeur réelle, écrite par Windows

    private readonly TempDirectory _dir = new();
    private readonly FakeAccessor _registry = new(KnownSettings.RegistryKind);
    private readonly ChangeJournal _journal;
    private readonly Guid _profile = Guid.NewGuid();

    public GameGraphicsJournalTests()
    {
        _journal = new ChangeJournal(new JsonStateStore<JournalDocument>(Path.Combine(_dir.Path, "fixes.json")), new SettingAccessors([_registry]));
        _registry.Set(KnownSettings.GpuPreference(PubgDotted), SettingValue.String(WindowsValue)); // seule entrée existante
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>Même lecture que GameGraphicsService : origine prise dans le journal si OptiGame a déjà modifié l'entrée.</summary>
    private List<GraphicsTarget> Targets() => GameGraphics.TargetNames(Pubg, [PubgDotted]).Select(name =>
    {
        var target = KnownSettings.GpuPreference(name);
        var current = _registry.Read(target);
        return new GraphicsTarget(name, _journal.Entries.FirstOrDefault(e => e.Target == target)?.Original ?? current, current);
    }).ToList();

    private string? Value(string name) => _registry.Read(KnownSettings.GpuPreference(name)).Text;

    [Fact]
    public void Switching_several_times_then_undoing_restores_windows_exact_values()
    {
        _journal.Apply(GameGraphics.Change(_profile, "PUBG", Targets(), AutoHdrChoice.On, GpuChoice.Windows)!);
        Assert.Equal("AppStatus=1;AutoHDREnable=1;", Value(PubgDotted));
        Assert.Equal("AutoHDREnable=1;", Value(Pubg)); // chemin normalisé, créé

        _journal.Apply(GameGraphics.Change(_profile, "PUBG", Targets(), AutoHdrChoice.Off, GpuChoice.HighPerformance)!);
        Assert.Equal("AppStatus=1;AutoHDREnable=0;GpuPreference=2;", Value(PubgDotted));
        Assert.Equal(GameGraphicsState(AutoHdrChoice.Off, "0", GpuChoice.HighPerformance), GameGraphics.Read(Value(PubgDotted)));

        var report = _journal.Undo(GameGraphics.ChangeId(_profile));

        Assert.True(report.Success);
        Assert.Equal(WindowsValue, Value(PubgDotted));                                  // 2097 et AppStatus retrouvés
        Assert.True(_registry.Read(KnownSettings.GpuPreference(Pubg)).IsAbsent);       // entrée créée par OptiGame : supprimée
        Assert.False(_journal.IsActive(GameGraphics.ChangeId(_profile)));
    }

    private static GameGraphicsState GameGraphicsState(AutoHdrChoice hdr, string raw, GpuChoice gpu) => new(hdr, raw, gpu);
}
