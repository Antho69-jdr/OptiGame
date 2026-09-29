using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Platform.Privileged;
using OptiGame.Platform.Registry;
using Win32Registry = Microsoft.Win32.Registry;

namespace OptiGame.Platform.Tests;

/// <summary>Registre réel, limité à une clé de test HKCU jetable.</summary>
public sealed class RegistrySettingAccessorTests : IDisposable
{
    private const string TestRoot = @"Software\OptiGame.Tests";
    private readonly string _key = $@"HKCU\{TestRoot}\{Guid.NewGuid():N}";
    private readonly RegistrySettingAccessor _accessor = new(new InProcessPrivilegedOperations());

    public void Dispose() => Win32Registry.CurrentUser.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);

    private SettingTarget Target(string name) => new(KnownSettings.RegistryKind, _key, name);

    [Fact]
    public void Missing_key_and_value_read_as_absent()
    {
        Assert.True(_accessor.Read(Target("Nope")).IsAbsent);
    }

    public static TheoryData<SettingValue> Values() =>
    [
        SettingValue.DWord(0),
        SettingValue.DWord(uint.MaxValue),
        SettingValue.QWord(ulong.MaxValue),
        SettingValue.String("AppStatus=1;AutoHDREnable=2097;"),
        SettingValue.ExpandString(@"%SystemRoot%\System32"),
        SettingValue.MultiString(["a", "b"]),
        SettingValue.Binary([0x90, 0x44, 0x7B]),
    ];

    [Theory]
    [MemberData(nameof(Values))]
    public void Write_then_read_round_trips(SettingValue value)
    {
        _accessor.Write(Target("V"), value);

        Assert.Equal(value, _accessor.Read(Target("V")));
    }

    [Fact]
    public void Writing_absent_deletes_the_value()
    {
        _accessor.Write(Target("V"), SettingValue.DWord(1));
        _accessor.Write(Target("V"), SettingValue.Absent);

        Assert.True(_accessor.Read(Target("V")).IsAbsent);
        _accessor.Write(Target("V"), SettingValue.Absent); // idempotent
    }

    [Fact]
    public void Value_names_with_backslashes_are_supported()
    {
        const string exe = @"A:\SteamLibrary\steamapps\common\Void Crew\Void Crew.exe";
        _accessor.Write(Target(exe), SettingValue.String("GpuPreference=2;"));

        Assert.Equal("GpuPreference=2;", _accessor.Read(Target(exe)).Text);
        Assert.Contains(exe, _accessor.GetValueNames(_key));
    }

    [Fact]
    public void Journal_applies_and_restores_real_registry_including_absent_original()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OptiGame.Tests", Guid.NewGuid().ToString("N"));
        var journal = new ChangeJournal(new JsonStateStore<JournalDocument>(Path.Combine(dir, "fixes.json")),
            new SettingAccessors([_accessor]));
        _accessor.Write(Target("Existing"), SettingValue.DWord(1));

        journal.Apply(new ReversibleChange
        {
            Id = "test",
            Title = "test",
            What = "test",
            Why = "test",
            Writes =
            [
                new SettingWrite(Target("Existing"), SettingValue.DWord(0)),
                new SettingWrite(Target("New"), SettingValue.DWord(1)),
            ],
        });
        Assert.Equal(SettingValue.DWord(0), _accessor.Read(Target("Existing")));
        Assert.Equal(SettingValue.DWord(1), _accessor.Read(Target("New")));

        Assert.True(journal.Undo("test").Success);

        Assert.Equal(SettingValue.DWord(1), _accessor.Read(Target("Existing")));
        Assert.True(_accessor.Read(Target("New")).IsAbsent);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Unsupported_hive_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() => _accessor.Read(new SettingTarget(KnownSettings.RegistryKind, @"HKCR\x", "y")));
    }
}
