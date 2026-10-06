using OptiGame.Core.Abstractions;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Sessions;

internal sealed class FakeProcessControl : IProcessControl
{
    public List<string> Closed { get; } = [];

    public Dictionary<int, GamePriority> Priorities { get; } = [];

    public string? PriorityError { get; set; }

    /// <summary>Instances en cours par chemin d'exe.</summary>
    public Dictionary<string, List<int>> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Close(string exeName)
    {
        Closed.Add(exeName);
        return 1;
    }

    public string? TrySetPriority(int processId, GamePriority priority)
    {
        if (PriorityError is not null) return PriorityError;
        Priorities[processId] = priority;
        return null;
    }

    public IReadOnlyList<int> FindProcesses(string exePath) => Running.GetValueOrDefault(exePath, []);
}

public sealed class GameSessionManagerTests : IDisposable
{
    private const string GameExe = @"A:\SteamLibrary\steamapps\common\PUBG\TslGame\Binaries\Win64\TslGame.exe";
    private const int GamePid = 4242;
    private static readonly Guid Atlas = new("11111111-1111-1111-1111-111111111111");

    private readonly TempDirectory _dir = new();
    private readonly FakeAccessor _power = new(KnownSettings.PowerSchemeKind);
    private readonly FakeAccessor _process = new(KnownSettings.ProcessKind);
    private readonly FakeAccessor _visual = new(KnownSettings.VisualEffectKind);
    private readonly FakeProcessControl _control = new();
    private readonly ProfileStore _profiles;

    public GameSessionManagerTests()
    {
        _profiles = new ProfileStore(new JsonStateStore<ProfilesDocument>(_dir.File("profiles.json")));
        _power.Set(KnownSettings.ActivePowerScheme, SettingValue.String(Atlas.ToString()));
    }

    public void Dispose() => _dir.Dispose();

    private static SettingValue Chrome => SettingValue.MultiString([@"C:\Program Files\Google\Chrome\Application\chrome.exe", "\"chrome.exe\" --flag"]);

    /// <summary>Nouvelle instance sur les mêmes fichiers = redémarrage d'OptiGame.</summary>
    private GameSessionManager NewManager()
    {
        var accessors = new SettingAccessors([_power, _process, _visual]);
        var journal = new ChangeJournal(new JsonStateStore<JournalDocument>(_dir.File("session.json")), accessors);
        var schemes = new FakePowerSchemes(Atlas,
            new PowerScheme(Atlas, "Atlas Power Scheme"), new PowerScheme(PowerSchemes.HighPerformance, "Haute performance"));
        return new GameSessionManager(journal, _profiles, accessors, schemes, _control, TimeProvider.System);
    }

    private GameProfile SaveProfile(Action<GameProfile>? configure = null)
    {
        var profile = new GameProfile
        {
            Name = "PUBG",
            ExePath = GameExe,
            PowerSchemeId = PowerSchemes.HighPerformance,
            Priority = GamePriority.High,
            ProcessesToClose =
            [
                new ProcessToClose { ExeName = "chrome.exe", Relaunch = true },
                new ProcessToClose { ExeName = "iCUE.exe", Relaunch = false },
            ],
        };
        configure?.Invoke(profile);
        _profiles.Save(profile);
        return profile;
    }

    private void GameRunning(params int[] pids) => _control.Running[GameExe] = [.. pids];

    [Fact]
    public void Game_start_applies_the_whole_profile()
    {
        SaveProfile();
        _process.Set(KnownSettings.RunningProcess("chrome.exe"), Chrome);
        var manager = NewManager();

        var report = manager.OnProcessStarted(GamePid, GameExe);

        Assert.NotNull(report);
        Assert.Empty(report.Warnings);
        Assert.Equal(PowerSchemes.HighPerformance.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
        Assert.True(_process.Read(KnownSettings.RunningProcess("chrome.exe")).IsAbsent);
        Assert.Equal(["iCUE.exe"], _control.Closed);
        Assert.Equal(GamePriority.High, _control.Priorities[GamePid]);
        Assert.Equal(GamePid, manager.Current?.ProcessId);
    }

    [Fact]
    public void Visual_effects_are_cut_during_the_game_and_restored_after()
    {
        SaveProfile(p => p.ReduceVisualEffects = true);
        _visual.Set(KnownSettings.ClientAreaAnimation, SettingValue.DWord(1));
        _visual.Set(KnownSettings.WindowAnimation, SettingValue.DWord(1)); // transparence : jamais réglée (valeur absente)
        var manager = NewManager();

        var report = manager.OnProcessStarted(GamePid, GameExe);

        Assert.Empty(report!.Warnings);
        Assert.Equal(SettingValue.DWord(0), _visual.Read(KnownSettings.ClientAreaAnimation));
        Assert.Equal(SettingValue.DWord(0), _visual.Read(KnownSettings.WindowAnimation));
        Assert.Equal(SettingValue.DWord(0), _visual.Read(KnownSettings.Transparency));

        manager.OnProcessExited(GamePid);

        Assert.Equal(SettingValue.DWord(1), _visual.Read(KnownSettings.ClientAreaAnimation));
        Assert.Equal(SettingValue.DWord(1), _visual.Read(KnownSettings.WindowAnimation));
        Assert.True(_visual.Read(KnownSettings.Transparency).IsAbsent); // restaurée par suppression
    }

    [Fact]
    public void Visual_effects_are_left_alone_unless_asked()
    {
        SaveProfile();
        _visual.Set(KnownSettings.ClientAreaAnimation, SettingValue.DWord(1));
        NewManager().OnProcessStarted(GamePid, GameExe);

        Assert.Equal(SettingValue.DWord(1), _visual.Read(KnownSettings.ClientAreaAnimation));
        Assert.DoesNotContain(SessionPlan.Describe(new GameProfile { Name = "x", ExePath = GameExe }, []), l => l.Contains("transparence"));
    }

    [Fact]
    public void Game_exit_restores_power_plan_and_relaunches_closed_programs()
    {
        SaveProfile();
        _process.Set(KnownSettings.RunningProcess("chrome.exe"), Chrome);
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);

        var (outcome, _) = manager.OnProcessExited(GamePid);

        Assert.Equal(ExitOutcome.Ended, outcome);
        Assert.Equal(Atlas.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
        Assert.Equal(Chrome, _process.Read(KnownSettings.RunningProcess("chrome.exe")));
        Assert.Null(manager.Current);
        Assert.False(File.Exists(_dir.File("session.json")));
    }

    [Fact]
    public void Program_that_was_not_running_is_not_journaled_so_it_is_not_closed_at_the_end()
    {
        // Chrome fermé au lancement du jeu, puis ouvert par l'utilisateur pendant la partie.
        SaveProfile();
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);
        _process.Set(KnownSettings.RunningProcess("chrome.exe"), Chrome);

        manager.OnProcessExited(GamePid);

        Assert.Equal(Chrome, _process.Read(KnownSettings.RunningProcess("chrome.exe")));
        Assert.DoesNotContain(_process.WriteLog, w => w.Value.IsAbsent);
    }

    [Fact]
    public void Unknown_or_disabled_games_are_ignored()
    {
        SaveProfile(p => p.Enabled = false);
        var manager = NewManager();

        Assert.Null(manager.OnProcessStarted(1, GameExe));
        Assert.Null(manager.OnProcessStarted(2, @"C:\Windows\notepad.exe"));
        Assert.Null(manager.Current);
        Assert.Empty(_power.WriteLog);
    }

    [Fact]
    public void Only_one_session_at_a_time()
    {
        SaveProfile();
        _profiles.Save(new GameProfile { Name = "Autre", ExePath = @"C:\Jeux\autre.exe", PowerSchemeId = Atlas });
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);

        Assert.Null(manager.OnProcessStarted(99, @"C:\Jeux\autre.exe"));
        Assert.Equal(GamePid, manager.Current?.ProcessId);
    }

    [Fact]
    public void Exit_of_another_process_is_ignored()
    {
        SaveProfile();
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);

        Assert.Equal(ExitOutcome.Ignored, manager.OnProcessExited(1234).Outcome);
        Assert.NotNull(manager.Current);
    }

    [Fact]
    public void Session_continues_when_another_instance_of_the_game_is_still_running()
    {
        SaveProfile();
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);
        GameRunning(GamePid, 5555);

        var (outcome, newPid) = manager.OnProcessExited(GamePid);

        Assert.Equal(ExitOutcome.Continued, outcome);
        Assert.Equal(5555, newPid);
        Assert.Equal(PowerSchemes.HighPerformance.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
    }

    [Fact]
    public void Crash_during_session_then_game_closed_restores_at_startup()
    {
        SaveProfile();
        _process.Set(KnownSettings.RunningProcess("chrome.exe"), Chrome);
        NewManager().OnProcessStarted(GamePid, GameExe);
        // OptiGame plante ; le jeu est fermé avant le redémarrage d'OptiGame.

        var (outcome, report, _) = NewManager().Recover();

        Assert.Equal(RecoveryOutcome.Restored, outcome);
        Assert.True(report!.AfterCrash);
        Assert.True(report.Success);
        Assert.Equal(Atlas.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
        Assert.Equal(Chrome, _process.Read(KnownSettings.RunningProcess("chrome.exe")));
    }

    [Fact]
    public void Crash_during_session_with_game_still_running_resumes_the_session()
    {
        SaveProfile();
        NewManager().OnProcessStarted(GamePid, GameExe);
        GameRunning(GamePid);

        var manager = NewManager();
        var (outcome, _, pid) = manager.Recover();

        Assert.Equal(RecoveryOutcome.Resumed, outcome);
        Assert.Equal(GamePid, pid);
        Assert.Equal(PowerSchemes.HighPerformance.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);

        _control.Running.Clear();
        Assert.Equal(ExitOutcome.Ended, manager.OnProcessExited(GamePid).Outcome);
        Assert.Equal(Atlas.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
    }

    [Fact]
    public void Nothing_to_recover_on_a_clean_start()
    {
        Assert.Equal(RecoveryOutcome.Nothing, NewManager().Recover().Outcome);
    }

    [Fact]
    public void Failed_relaunch_is_reported_and_not_kept_pending()
    {
        SaveProfile();
        _process.Set(KnownSettings.RunningProcess("chrome.exe"), Chrome);
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);
        _process.FailWrites.Add(KnownSettings.RunningProcess("chrome.exe"));

        manager.OnProcessExited(GamePid);

        Assert.False(File.Exists(_dir.File("session.json")));
        Assert.Equal(Atlas.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
    }

    [Fact]
    public void Failed_power_restore_stays_pending_for_retry()
    {
        SaveProfile();
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);
        _power.FailWrites.Add(KnownSettings.ActivePowerScheme);

        manager.OnProcessExited(GamePid);

        Assert.True(File.Exists(_dir.File("session.json")));
        _power.FailWrites.Clear();
        Assert.True(NewManager().EndNow()!.Success);
        Assert.Equal(Atlas.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
    }

    [Fact]
    public void Failed_step_becomes_a_warning_without_stopping_the_others()
    {
        SaveProfile();
        _power.FailWrites.Add(KnownSettings.ActivePowerScheme);
        _control.PriorityError = "Accès refusé (anti-cheat)";
        var manager = NewManager();

        var report = manager.OnProcessStarted(GamePid, GameExe)!;

        Assert.Equal(2, report.Warnings.Count);
        Assert.Contains("iCUE.exe", _control.Closed);
    }

    [Fact]
    public void End_now_restores_immediately()
    {
        SaveProfile();
        var manager = NewManager();
        manager.OnProcessStarted(GamePid, GameExe);

        var report = manager.EndNow();

        Assert.NotNull(report);
        Assert.Null(manager.Current);
        Assert.Equal(Atlas.ToString(), _power.Read(KnownSettings.ActivePowerScheme).Text);
        Assert.Null(manager.EndNow());
    }
}
