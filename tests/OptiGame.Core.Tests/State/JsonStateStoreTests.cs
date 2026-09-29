using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.State;

public sealed class JsonStateStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Load_returns_null_when_missing()
    {
        Assert.Null(new JsonStateStore<JournalDocument>(_dir.File("missing.json")).Load());
    }

    [Fact]
    public void Save_creates_directory_and_leaves_no_temp_file()
    {
        var path = Path.Combine(_dir.Path, "sub", "state.json");
        new JsonStateStore<JournalDocument>(path).Save(new JournalDocument { Context = "x" });

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Leftover_temp_file_is_ignored_and_original_wins()
    {
        var path = _dir.File("state.json");
        var store = new JsonStateStore<JournalDocument>(path);
        store.Save(new JournalDocument { Context = "original" });
        File.WriteAllText(path + ".tmp", "{ \"context\": \"écriture inter"); // écriture interrompue

        Assert.Equal("original", store.Load()!.Context);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_throws_and_is_left_untouched()
    {
        var path = _dir.File("state.json");
        File.WriteAllText(path, "{ pas du json");

        var ex = Assert.Throws<StateFileCorruptException>(() => new JsonStateStore<JournalDocument>(path).Load());

        Assert.Equal(path, ex.FilePath);
        Assert.Equal("{ pas du json", File.ReadAllText(path));
    }

    public static TheoryData<SettingValue> AllKinds() =>
    [
        SettingValue.Absent,
        SettingValue.DWord(0),
        SettingValue.DWord(uint.MaxValue),
        SettingValue.QWord(ulong.MaxValue),
        SettingValue.String("AutoHDREnable=2097;"),
        SettingValue.ExpandString(@"%SystemRoot%\x"),
        SettingValue.MultiString(["a", "b"]),
        SettingValue.Binary([0x90, 0x44, 0x7B]),
    ];

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_value_kind_round_trips(SettingValue value)
    {
        var path = _dir.File("state.json");
        var store = new JsonStateStore<JournalDocument>(path);
        var target = new SettingTarget("registry", @"HKCU\X", "Y");
        store.Save(new JournalDocument
        {
            Entries = [new JournalEntry { Target = target, Original = value, Applied = value }],
        });

        var entry = Assert.Single(store.Load()!.Entries);

        Assert.Equal(value, entry.Original);
        Assert.Equal(target, entry.Target);
    }

    [Fact]
    public void DWord_keeps_unsigned_semantics()
    {
        Assert.Equal(uint.MaxValue, SettingValue.DWord(uint.MaxValue).AsDWord());
        Assert.Null(SettingValue.Absent.AsDWord());
        Assert.NotEqual(SettingValue.DWord(0), SettingValue.Absent);
    }
}
