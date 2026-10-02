using OptiGame.Core.Abstractions;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Diagnostics.Checks;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Diagnostics;

public sealed class CheckTests
{
    private readonly FakeAccessor _registry = new(KnownSettings.RegistryKind);

    private SettingAccessors Settings => new([_registry]);

    // ---- Mode Jeu ----

    [Fact]
    public void GameMode_absent_value_means_enabled()
    {
        var result = new GameModeCheck(Settings).Run();

        Assert.Equal(DiagnosticStatus.Ok, result.Status);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void GameMode_disabled_offers_fix_to_enable()
    {
        _registry.Set(KnownSettings.GameMode, SettingValue.DWord(0));

        var result = new GameModeCheck(Settings).Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        var write = Assert.Single(Assert.Single(result.Fixes).Change.Writes);
        Assert.Equal(KnownSettings.GameMode, write.Target);
        Assert.Equal(SettingValue.DWord(1), write.NewValue);
    }

    // ---- Enregistrement en arrière-plan ----

    [Fact]
    public void Background_recording_enabled_offers_fix_touching_only_historical_capture()
    {
        _registry.Set(KnownSettings.BackgroundRecording, SettingValue.DWord(1));
        _registry.Set(KnownSettings.AppCapture, SettingValue.DWord(1));
        _registry.Set(KnownSettings.GameDvrEnabled, SettingValue.DWord(1));

        var result = new BackgroundRecordingCheck(Settings).Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        var write = Assert.Single(Assert.Single(result.Fixes).Change.Writes);
        Assert.Equal(KnownSettings.BackgroundRecording, write.Target);
        Assert.Equal(SettingValue.DWord(0), write.NewValue);
    }

    [Theory]
    [InlineData(false, 1u, 1u)] // HistoricalCaptureEnabled absent = désactivé
    [InlineData(true, 0u, 1u)]  // stratégie AllowGameDVR = 0
    [InlineData(true, 1u, 0u)]  // interrupteur global coupé
    public void Background_recording_is_ok_when_off_or_blocked(bool historicalOn, uint policy, uint global)
    {
        if (historicalOn) _registry.Set(KnownSettings.BackgroundRecording, SettingValue.DWord(1));
        _registry.Set(KnownSettings.GameDvrPolicy, SettingValue.DWord(policy));
        _registry.Set(KnownSettings.GameDvrEnabled, SettingValue.DWord(global));

        Assert.Equal(DiagnosticStatus.Ok, new BackgroundRecordingCheck(Settings).Run().Status);
    }

    // ---- RAM ----

    private static MemoryModule Module(string bank, string locator, uint? speed, uint? configured, uint type = 26) =>
        new(bank, locator, 8UL << 30, speed, configured, "F4-3600C18-8GVK", type);

    [Fact]
    public void Ram_below_rated_speed_needs_attention_without_fix()
    {
        var result = new RamSpeedCheck(new FakeMemory(2, Module("P0 CHANNEL A", "DIMM 0", 3600, 2133))).Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        Assert.Contains("XMP", result.Explanation);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Ram_at_low_jedec_speed_is_info()
    {
        var result = new RamSpeedCheck(new FakeMemory(2, Module("", "DIMM_A1", 2133, 2133))).Run();

        Assert.Equal(DiagnosticStatus.Info, result.Status);
    }

    [Fact]
    public void Ram_at_rated_speed_is_ok()
    {
        // Relevé réel : 4 × F4-3600C18, 3600/3600.
        var result = new RamSpeedCheck(new FakeMemory(4, Module("P0 CHANNEL A", "DIMM 0", 3600, 3600))).Run();

        Assert.Equal(DiagnosticStatus.Ok, result.Status);
    }

    [Theory]
    [InlineData("P0 CHANNEL A", "DIMM 0", "A")]
    [InlineData("BANK 0", "ChannelB-DIMM0", "B")]
    [InlineData("", "Controller0-ChannelA-DIMM1", "A")]
    [InlineData("", "DIMM_B2", "B")]
    [InlineData("BANK 2", "DIMM 3", null)]
    public void Channel_is_parsed_from_slot_labels(string bank, string locator, string? expected)
    {
        Assert.Equal(expected, MemoryChannelCheck.ChannelOf(Module(bank, locator, 3200, 3200)));
    }

    [Fact]
    public void Single_module_needs_attention_and_is_an_estimate()
    {
        var result = new MemoryChannelCheck(new FakeMemory(4, Module("", "DIMM_A2", 3200, 3200))).Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        Assert.True(result.IsEstimate);
    }

    [Fact]
    public void Modules_on_two_channels_are_ok()
    {
        var result = new MemoryChannelCheck(new FakeMemory(4,
            Module("P0 CHANNEL A", "DIMM 1", 3600, 3600),
            Module("P0 CHANNEL B", "DIMM 1", 3600, 3600))).Run();

        Assert.Equal(DiagnosticStatus.Ok, result.Status);
    }

    [Fact]
    public void Modules_on_same_channel_need_attention()
    {
        var result = new MemoryChannelCheck(new FakeMemory(4,
            Module("", "DIMM_A1", 3200, 3200),
            Module("", "DIMM_A2", 3200, 3200))).Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
    }

    [Fact]
    public void Unparseable_slots_are_info()
    {
        var result = new MemoryChannelCheck(new FakeMemory(null,
            Module("BANK 0", "DIMM 0", 3200, 3200),
            Module("BANK 1", "DIMM 1", 3200, 3200))).Run();

        Assert.Equal(DiagnosticStatus.Info, result.Status);
    }

    // ---- Pilote GPU ----

    private static readonly GpuAdapter Rtx3070 =
        new("NVIDIA GeForce RTX 3070", "32.0.15.9186", new DateTime(2026, 1, 20), @"PCI\VEN_10DE&DEV_2488");

    [Fact]
    public void Driver_older_than_six_months_needs_attention()
    {
        var check = new GpuDriverAgeCheck(new FakeGpus(Rtx3070), new FixedTime(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)));

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        Assert.Contains("8 mois", result.Summary);
    }

    [Fact]
    public void Recent_driver_is_ok_and_virtual_adapters_are_ignored()
    {
        var virtualAdapter = new GpuAdapter("Parsec Virtual Display", "1.0", new DateTime(2020, 1, 1), @"ROOT\DISPLAY\0000");
        var check = new GpuDriverAgeCheck(new FakeGpus(Rtx3070, virtualAdapter), new FixedTime(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(DiagnosticStatus.Ok, check.Run().Status);
    }

    // ---- Plan d'alimentation ----

    private static readonly PowerScheme[] StandardSchemes =
    [
        new(PowerSchemes.Balanced, "Utilisation normale"),
        new(PowerSchemes.HighPerformance, "Haute performance"),
        new(PowerSchemes.PowerSaver, "Économie d'énergie"),
    ];

    [Fact]
    public void Power_saver_offers_switch_to_high_performance()
    {
        var result = new PowerPlanCheck(new FakePowerSchemes(PowerSchemes.PowerSaver, StandardSchemes)).Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        var write = Assert.Single(Assert.Single(result.Fixes).Change.Writes);
        Assert.Equal(KnownSettings.ActivePowerScheme, write.Target);
        Assert.Equal(PowerSchemes.HighPerformance.ToString(), write.NewValue.Text);
    }

    [Fact]
    public void Custom_scheme_is_info_without_fix()
    {
        var atlas = new Guid("11111111-1111-1111-1111-111111111111");
        var result = new PowerPlanCheck(new FakePowerSchemes(atlas, [new(atlas, "Atlas Power Scheme"), .. StandardSchemes])).Run();

        Assert.Equal(DiagnosticStatus.Info, result.Status);
        Assert.Contains("Atlas Power Scheme", result.Summary);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Balanced_is_info_and_high_performance_is_ok()
    {
        Assert.Equal(DiagnosticStatus.Info, new PowerPlanCheck(new FakePowerSchemes(PowerSchemes.Balanced, StandardSchemes)).Run().Status);
        Assert.Equal(DiagnosticStatus.Ok, new PowerPlanCheck(new FakePowerSchemes(PowerSchemes.HighPerformance, StandardSchemes)).Run().Status);
    }

    // ---- Boost du processeur ----

    private static readonly Guid Atlas = new("11111111-1111-1111-1111-111111111111");

    private static FakePowerSchemes AtlasWith(uint? boost, uint? max)
    {
        var power = new FakePowerSchemes(Atlas, [new(Atlas, "Atlas Power Scheme"), .. StandardSchemes]);
        if (boost is { } b) power.AcValues[(Atlas, PowerSettings.PerfBoostMode)] = b;
        if (max is { } m) power.AcValues[(Atlas, PowerSettings.ProcThrottleMax)] = m;
        return power;
    }

    [Fact]
    public void Boost_of_the_dev_machine_is_ok()
    {
        var result = new CpuBoostCheck(AtlasWith(boost: 2, max: 100)).Run(); // valeurs relevées le 2026-10-02
        Assert.Equal(DiagnosticStatus.Ok, result.Status);
        Assert.Contains("Offensif", result.Summary);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Disabled_boost_and_capped_max_state_are_fixed_back_to_windows_defaults()
    {
        var result = new CpuBoostCheck(AtlasWith(boost: 0, max: 99)).Run(); // « 99 % » : astuce courante pour couper le turbo

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        Assert.Equal(["fix.power.boost", "fix.power.maxstate"], result.Fixes.Select(f => f.Change.Id));
        var boost = Assert.Single(result.Fixes[0].Change.Writes);
        Assert.Equal(KnownSettings.PowerSetting(Atlas, PowerSettings.SubProcessor, PowerSettings.PerfBoostMode), boost.Target);
        Assert.Equal(2u, boost.NewValue.AsDWord());
        Assert.Equal(100u, Assert.Single(result.Fixes[1].Change.Writes).NewValue.AsDWord());
        Assert.All(result.Fixes, f => Assert.True(f.Change.RequiresAdmin));
    }

    [Fact]
    public void Boost_missing_from_the_plan_is_info()
    {
        Assert.Equal(DiagnosticStatus.Info, new CpuBoostCheck(AtlasWith(boost: null, max: null)).Run().Status);
    }

    // ---- Alimentation ----

    [Fact]
    public void Laptop_on_battery_needs_attention()
    {
        var result = new PowerSourceCheck(new FakePowerStatus(new PowerStatus(true, false, 80, false))).Run();
        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
    }

    [Fact]
    public void Desktop_without_energy_saver_is_ok()
    {
        var result = new PowerSourceCheck(new FakePowerStatus(new PowerStatus(false, true, null, false))).Run();
        Assert.Equal(DiagnosticStatus.Ok, result.Status);
    }

    // ---- HAGS ----

    [Fact]
    public void Hags_enabled_with_absent_registry_value_is_ok()
    {
        // Relevé réel : HwSchMode absent, mais le pilote rapporte HAGS activé.
        var result = new HagsCheck(new FakeScheduling(new GpuSchedulingState("RTX 3070", true, true)), Settings).Run();

        Assert.Equal(DiagnosticStatus.Ok, result.Status);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Hags_supported_but_disabled_offers_admin_reboot_fix()
    {
        var result = new HagsCheck(new FakeScheduling(new GpuSchedulingState("RTX 3070", true, false)), Settings).Run();

        Assert.Equal(DiagnosticStatus.Info, result.Status);
        var change = Assert.Single(result.Fixes).Change;
        Assert.True(change.RequiresAdmin);
        Assert.True(change.RequiresReboot);
        Assert.Equal(SettingValue.DWord(2), Assert.Single(change.Writes).NewValue);
    }

    [Fact]
    public void Hags_pending_activation_is_reported_without_fix()
    {
        _registry.Set(KnownSettings.HwSchMode, SettingValue.DWord(2));

        var result = new HagsCheck(new FakeScheduling(new GpuSchedulingState("RTX 3070", true, false)), Settings).Run();

        Assert.Contains("redémarrage", result.Summary);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Hags_unsupported_is_info()
    {
        var result = new HagsCheck(new FakeScheduling(new GpuSchedulingState("GTX 960", false, false)), Settings).Run();
        Assert.Equal(DiagnosticStatus.Info, result.Status);
        Assert.Empty(result.Fixes);
    }

    // ---- Intégrité de la mémoire ----

    [Fact]
    public void Memory_integrity_running_is_info_with_advanced_fix_only()
    {
        _registry.Set(KnownSettings.HvciEnabled, SettingValue.DWord(1));

        var result = new MemoryIntegrityCheck(new FakeDeviceGuard(new DeviceGuardStatus(2, [2], [2])), Settings).Run();

        Assert.Equal(DiagnosticStatus.Info, result.Status);
        var fix = Assert.Single(result.Fixes);
        Assert.True(fix.IsAdvanced);
        Assert.NotNull(fix.Change.Warning);
        Assert.True(fix.Change.RequiresReboot);
    }

    [Fact]
    public void Locked_memory_integrity_offers_no_fix()
    {
        _registry.Set(KnownSettings.HvciEnabled, SettingValue.DWord(1));
        _registry.Set(KnownSettings.HvciLocked, SettingValue.DWord(1));

        var result = new MemoryIntegrityCheck(new FakeDeviceGuard(new DeviceGuardStatus(2, [2], [2])), Settings).Run();

        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Memory_integrity_off_is_info_without_fix()
    {
        // Relevé réel : VBS actif (2), HVCI non actif, Enabled = 0.
        _registry.Set(KnownSettings.HvciEnabled, SettingValue.DWord(0));

        var result = new MemoryIntegrityCheck(new FakeDeviceGuard(new DeviceGuardStatus(2, [0], [0])), Settings).Run();

        Assert.Equal(DiagnosticStatus.Info, result.Status);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Missing_device_guard_is_info()
    {
        Assert.Equal(DiagnosticStatus.Info, new MemoryIntegrityCheck(new FakeDeviceGuard(null), Settings).Run().Status);
    }

    // ---- Préférence GPU ----

    [Fact]
    public void Single_gpu_makes_gpu_preference_not_applicable()
    {
        var result = new GpuPreferenceCheck(new FakeGpus(Rtx3070), new FakeRegistryReader([]), Settings).Run();

        Assert.Equal(DiagnosticStatus.Info, result.Status);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Hybrid_pc_offers_merged_high_performance_preference()
    {
        const string exe = @"A:\SteamLibrary\steamapps\common\Void Crew\Void Crew.exe";
        var igpu = new GpuAdapter("AMD Radeon Graphics", "31.0", DateTime.Today, @"PCI\VEN_1002&DEV_1681");
        _registry.Set(KnownSettings.GpuPreference(exe), SettingValue.String("AppStatus=1;AutoHDREnable=2097;"));
        var reader = new FakeRegistryReader(new() { [KnownSettings.GpuPreferencesKey] = [KnownSettings.GpuPreferencesGlobalValue, exe] });

        var result = new GpuPreferenceCheck(new FakeGpus(Rtx3070, igpu), reader, Settings).Run();

        var write = Assert.Single(Assert.Single(result.Fixes).Change.Writes);
        Assert.Equal(exe, write.Target.Name);
        Assert.Equal("AppStatus=1;AutoHDREnable=2097;GpuPreference=2;", write.NewValue.Text);
    }

    // ---- Écrans ----

    [Fact]
    public void Display_below_max_refresh_offers_mode_change()
    {
        var result = new DisplayRefreshRateCheck(new FakeDisplays(
            new DisplayInfo(@"\\.\DISPLAY1", "Generic PnP Monitor", 3440, 1440, 60, 165),
            new DisplayInfo(@"\\.\DISPLAY2", "Generic PnP Monitor", 1080, 1920, 60, 60))).Run();

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        var write = Assert.Single(Assert.Single(result.Fixes).Change.Writes);
        Assert.Equal(KnownSettings.DisplayMode(@"\\.\DISPLAY1"), write.Target);
        Assert.Equal("3440x1440@165", write.NewValue.Text);
    }

    [Fact]
    public void Displays_at_max_refresh_are_ok()
    {
        var result = new DisplayRefreshRateCheck(new FakeDisplays(
            new DisplayInfo(@"\\.\DISPLAY1", "", 3440, 1440, 165, 165))).Run();

        Assert.Equal(DiagnosticStatus.Ok, result.Status);
    }

    // ---- Runner ----

    [Fact]
    public void Runner_turns_exceptions_into_error_results()
    {
        _registry.FailReads.Add(KnownSettings.GameMode);

        var result = Assert.Single(new DiagnosticRunner([new GameModeCheck(Settings)]).RunAll());

        Assert.Equal(DiagnosticStatus.Error, result.Status);
    }
}
