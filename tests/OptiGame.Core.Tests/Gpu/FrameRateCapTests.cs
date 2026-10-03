using OptiGame.Core.Gpu;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Gpu;

/// <summary>Plafond de FPS du pilote NVIDIA, avec le vrai journal des corrections (le pilote est remplacé par un faux accesseur).</summary>
public sealed class FrameRateCapTests : IDisposable
{
    private const string VoidCrew = @"A:\SteamLibrary\steamapps\common\Void Crew\Void Crew.exe";

    private readonly TempDirectory _dir = new();
    private readonly FakeAccessor _driver = new(KnownSettings.NvidiaProfileKind);
    private readonly ChangeJournal _journal;
    private readonly Guid _profile = Guid.NewGuid();

    public FrameRateCapTests() =>
        _journal = new ChangeJournal(new JsonStateStore<JournalDocument>(Path.Combine(_dir.Path, "fixes.json")), new SettingAccessors([_driver]));

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(165, 162)] // écran de la machine de dev
    [InlineData(60, 57)]
    [InlineData(21, 20)]
    public void Suggests_just_below_the_refresh_rate(int hz, int fps) => Assert.Equal(fps, FrameRateCap.Suggested(hz));

    [Theory]
    [InlineData(162, true)]
    [InlineData(0, true)]   // aucun plafond
    [InlineData(10, false)]
    [InlineData(1001, false)]
    [InlineData(null, false)]
    public void Validates_the_input(int? fps, bool valid) => Assert.Equal(valid, FrameRateCap.Validate(fps) is null);

    [Fact]
    public void Writes_the_documented_setting_and_describes_the_profile()
    {
        var created = FrameRateCap.Change(_profile, "Void Crew", VoidCrew, null, null, 162);
        var write = Assert.Single(created.Writes);
        Assert.Equal(new SettingTarget(KnownSettings.NvidiaProfileKind, VoidCrew, "0x10835002"), write.Target);
        Assert.Equal(162u, write.NewValue.AsDWord());
        Assert.Contains("nouveau profil « OptiGame - Void Crew.exe »", created.What);
        Assert.Contains("aucun plafond → 162 FPS", created.What);

        var existing = FrameRateCap.Change(_profile, "Overwatch", @"A:\x\Overwatch.exe", "Overwatch 2", 120, 0);
        Assert.Contains("profil « Overwatch 2 »", existing.What);
        Assert.Contains("120 FPS → aucun plafond", existing.What);
    }

    [Fact]
    public void Undo_removes_the_setting_the_profile_did_not_have()
    {
        var target = KnownSettings.NvidiaFrameRateLimit(VoidCrew);

        _journal.Apply(FrameRateCap.Change(_profile, "Void Crew", VoidCrew, null, null, 162));
        Assert.Equal(162u, _driver.Read(target).AsDWord());
        _journal.Apply(FrameRateCap.Change(_profile, "Void Crew", VoidCrew, "OptiGame - void crew.exe", 162, 140)); // nouvel essai
        Assert.Equal(140u, _driver.Read(target).AsDWord());

        Assert.True(_journal.Undo(FrameRateCap.ChangeId(_profile)).Success);
        Assert.True(_driver.Read(target).IsAbsent); // réglage d'origine : non défini (le profil vide est alors supprimé)
        Assert.Empty(_journal.ActiveChanges);
    }
}
