using OptiGame.Core.Changes;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.State;

public sealed class ChangeJournalTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly FakeAccessor _system = new();
    private readonly SettingTarget _gameMode;
    private readonly SettingTarget _dvr;

    public ChangeJournalTests()
    {
        _gameMode = _system.Target(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled");
        _dvr = _system.Target(@"HKCU\System\GameConfigStore", "GameDVR_Enabled");
    }

    public void Dispose() => _dir.Dispose();

    private string JournalPath => _dir.File("session.json");

    /// <summary>Nouvelle instance sur le même fichier = ce que verrait l'appli au redémarrage après un crash.</summary>
    private ChangeJournal OpenJournal() =>
        new(new JsonStateStore<JournalDocument>(JournalPath), new SettingAccessors([_system]));

    private static ReversibleChange Change(string id, params SettingWrite[] writes) => new()
    {
        Id = id,
        Title = id,
        What = "test",
        Why = "test",
        Writes = writes,
    };

    [Fact]
    public void Apply_writes_new_value_and_journals_original()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();

        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));

        Assert.Equal(SettingValue.DWord(0), _system.Read(_dvr));
        var entry = Assert.Single(OpenJournal().Entries);
        Assert.Equal(_dvr, entry.Target);
        Assert.Equal(SettingValue.DWord(1), entry.Original);
        Assert.Equal(SettingValue.DWord(0), entry.Applied);
        Assert.Equal(["c1"], entry.ChangeIds);
    }

    [Fact]
    public void Original_is_persisted_before_the_first_write()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        JournalEntry? seenOnDiskBeforeWrite = null;
        _system.BeforeWrite = (_, _) => seenOnDiskBeforeWrite ??= OpenJournal().Entries.SingleOrDefault();

        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));

        Assert.NotNull(seenOnDiskBeforeWrite);
        Assert.Equal(SettingValue.DWord(1), seenOnDiskBeforeWrite.Original);
    }

    [Fact]
    public void Absent_value_is_journaled_as_absent_and_restored_by_deletion()
    {
        // Cas réel : AutoGameModeEnabled n'existe pas sur la machine de dev.
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_gameMode, SettingValue.DWord(1))));
        Assert.Equal(SettingValue.DWord(1), _system.Read(_gameMode));

        var report = journal.RestoreAll();

        Assert.True(report.Success);
        Assert.True(_system.Read(_gameMode).IsAbsent);
        Assert.Equal(SettingValue.Absent, _system.WriteLog[^1].Value);
    }

    [Fact]
    public void RestoreAll_restores_in_reverse_order_and_deletes_the_journal()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        journal.Apply(Change("c2", new SettingWrite(_gameMode, SettingValue.DWord(1))));
        _system.WriteLog.Clear();

        var report = journal.RestoreAll();

        Assert.True(report.Success);
        Assert.Equal([_gameMode, _dvr], _system.WriteLog.Select(w => w.Target));
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
        Assert.False(File.Exists(JournalPath));
        Assert.False(journal.HasPendingEntries);
    }

    [Fact]
    public void Crash_after_apply_is_recovered_by_a_new_instance()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        OpenJournal().Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        // L'appli « plante » : l'instance est perdue sans restauration.

        var afterRestart = OpenJournal();
        Assert.True(afterRestart.HasPendingEntries);
        var report = afterRestart.RestoreAll();

        Assert.True(report.Success);
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void Crash_between_capture_and_apply_restores_harmlessly()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        // Arrêt brutal juste avant l'écriture : on fige l'état du disque à cet instant, puis on interrompt.
        var diskAtCrash = _dir.File("disk-at-crash.json");
        _system.BeforeWrite = (_, _) =>
        {
            File.Copy(JournalPath, diskAtCrash);
            throw new OperationCanceledException("crash simulé");
        };
        Assert.Throws<OperationCanceledException>(() =>
            journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0)))));
        _system.BeforeWrite = null;
        // Un vrai crash n'aurait exécuté aucun rollback : on remet le disque dans l'état figé.
        File.Copy(diskAtCrash, JournalPath, overwrite: true);

        // Redémarrage : le journal est présent, et le rejouer est sans danger.
        var afterRestart = OpenJournal();
        Assert.True(afterRestart.HasPendingEntries);
        var report = afterRestart.RestoreAll();
        Assert.True(report.Success);
        Assert.Empty(report.ModifiedExternally);
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
    }

    [Fact]
    public void Journal_left_on_disk_before_any_write_restores_to_the_same_value()
    {
        // Journal écrit, puis crash avant Write : on reproduit l'état disque sans passer par Apply.
        _system.Set(_dvr, SettingValue.DWord(1));
        new JsonStateStore<JournalDocument>(JournalPath).Save(new JournalDocument
        {
            Entries =
            [
                new JournalEntry
                {
                    Target = _dvr,
                    Original = SettingValue.DWord(1),
                    Applied = SettingValue.DWord(0),
                    ChangeIds = ["c1"],
                },
            ],
        });

        var report = OpenJournal().RestoreAll();

        Assert.True(report.Success);
        Assert.Empty(report.ModifiedExternally);
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
    }

    [Fact]
    public void Second_change_on_same_setting_keeps_the_oldest_original()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();

        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        journal.Apply(Change("c2", new SettingWrite(_dvr, SettingValue.DWord(5))));

        var entry = Assert.Single(journal.Entries);
        Assert.Equal(SettingValue.DWord(1), entry.Original);
        Assert.Equal(SettingValue.DWord(5), entry.Applied);
        Assert.Equal(["c1", "c2"], entry.ChangeIds);

        journal.RestoreAll();
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
    }

    [Fact]
    public void Failed_write_rolls_back_previous_writes_of_the_same_change()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        _system.FailWrites.Add(_gameMode);
        var journal = OpenJournal();

        Assert.Throws<UnauthorizedAccessException>(() => journal.Apply(Change("c1",
            new SettingWrite(_dvr, SettingValue.DWord(0)),
            new SettingWrite(_gameMode, SettingValue.DWord(1)))));

        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
        Assert.False(journal.HasPendingEntries);
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void Failed_read_during_capture_changes_nothing()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        _system.FailReads.Add(_gameMode);
        var journal = OpenJournal();

        Assert.Throws<IOException>(() => journal.Apply(Change("c1",
            new SettingWrite(_dvr, SettingValue.DWord(0)),
            new SettingWrite(_gameMode, SettingValue.DWord(1)))));

        Assert.Empty(_system.WriteLog);
        Assert.False(journal.HasPendingEntries);
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void Partial_restore_failure_keeps_failed_entry_and_restores_the_rest()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        journal.Apply(Change("c2", new SettingWrite(_gameMode, SettingValue.DWord(1))));
        _system.FailWrites.Add(_gameMode);

        var report = journal.RestoreAll();

        Assert.False(report.Success);
        Assert.Equal(_gameMode, Assert.Single(report.Failed).Target);
        Assert.Equal([_dvr], report.Restored);
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));

        // Le reste à faire est toujours sur disque, et une nouvelle tentative réussit.
        var retry = OpenJournal();
        Assert.Equal(_gameMode, Assert.Single(retry.Entries).Target);
        _system.FailWrites.Clear();
        Assert.True(retry.RestoreAll().Success);
        Assert.True(_system.Read(_gameMode).IsAbsent);
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void Undo_restores_only_the_given_change()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        journal.Apply(Change("c2", new SettingWrite(_gameMode, SettingValue.DWord(1))));

        var report = journal.Undo("c1");

        Assert.True(report.Success);
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
        Assert.Equal(SettingValue.DWord(1), _system.Read(_gameMode));
        Assert.False(journal.IsActive("c1"));
        Assert.True(journal.IsActive("c2"));
        Assert.True(OpenJournal().IsActive("c2"));
    }

    [Fact]
    public void Undo_of_a_shared_setting_waits_for_the_last_change()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        journal.Apply(Change("c2", new SettingWrite(_dvr, SettingValue.DWord(0))));

        var first = journal.Undo("c1");
        Assert.Equal([_dvr], first.StillHeld);
        Assert.Equal(SettingValue.DWord(0), _system.Read(_dvr));

        var second = journal.Undo("c2");
        Assert.Equal([_dvr], second.Restored);
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void Failed_undo_keeps_the_change_active()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        _system.FailWrites.Add(_dvr);

        var report = journal.Undo("c1");

        Assert.False(report.Success);
        Assert.True(journal.IsActive("c1"));
        Assert.True(OpenJournal().IsActive("c1"));
    }

    [Fact]
    public void External_modification_is_reported_and_original_still_restored()
    {
        _system.Set(_dvr, SettingValue.DWord(1));
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        _system.Set(_dvr, SettingValue.DWord(7)); // modifié par l'utilisateur ou un autre outil

        var report = journal.RestoreAll();

        Assert.Equal([_dvr], report.ModifiedExternally);
        Assert.Equal(SettingValue.DWord(1), _system.Read(_dvr));
    }

    [Fact]
    public void Active_changes_are_described_after_restart_and_pruned_after_undo()
    {
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        journal.Apply(Change("c2", new SettingWrite(_gameMode, SettingValue.DWord(1))));

        var afterRestart = OpenJournal();
        Assert.Equal(["c1", "c2"], afterRestart.ActiveChanges.Select(c => c.Id));
        Assert.Equal("c1", afterRestart.ActiveChanges[0].Title);

        afterRestart.Undo("c1");
        Assert.Equal(["c2"], OpenJournal().ActiveChanges.Select(c => c.Id));

        afterRestart.RestoreAll();
        Assert.Empty(afterRestart.ActiveChanges);
    }

    [Fact]
    public void Failed_apply_leaves_no_change_record()
    {
        _system.FailWrites.Add(_dvr);
        var journal = OpenJournal();

        Assert.Throws<UnauthorizedAccessException>(() => journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0)))));

        Assert.Empty(journal.ActiveChanges);
    }

    [Fact]
    public void Shared_setting_record_disappears_only_with_its_last_setting()
    {
        var journal = OpenJournal();
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));
        journal.Apply(Change("c2", new SettingWrite(_dvr, SettingValue.DWord(0))));

        journal.Undo("c1");

        Assert.Equal(["c2"], journal.ActiveChanges.Select(c => c.Id));
    }

    [Fact]
    public void Context_is_persisted_with_the_entries()
    {
        var journal = OpenJournal();
        journal.SetContext("StarCitizen.exe");
        journal.Apply(Change("c1", new SettingWrite(_dvr, SettingValue.DWord(0))));

        Assert.Equal("StarCitizen.exe", OpenJournal().Context);
    }

    [Fact]
    public void Target_names_containing_backslashes_round_trip()
    {
        // Cas réel : UserGpuPreferences utilise le chemin de l'exe comme nom de valeur.
        var gpuPref = _system.Target(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences",
            @"A:\SteamLibrary\steamapps\common\Void Crew\Void Crew.exe");
        _system.Set(gpuPref, SettingValue.String("AppStatus=1;AutoHDREnable=2097;"));

        OpenJournal().Apply(Change("c1", new SettingWrite(gpuPref, SettingValue.String("AppStatus=1;AutoHDREnable=2097;GpuPreference=2;"))));
        OpenJournal().RestoreAll();

        Assert.Equal(SettingValue.String("AppStatus=1;AutoHDREnable=2097;"), _system.Read(gpuPref));
    }
}
