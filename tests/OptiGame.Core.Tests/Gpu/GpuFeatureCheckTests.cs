using OptiGame.Core.Abstractions;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Diagnostics.Checks;
using OptiGame.Core.Gpu;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Gpu;

public sealed class GpuFeatureCheckTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Gpu", "Samples", name));

    private static readonly GpuAdapter Rtx3070 = new("NVIDIA GeForce RTX 3070", "32.0.15.9186", null, @"PCI\VEN_10DE&DEV_2488&SUBSYS_861719DA&REV_A1\4&1FC990D7&0&0019");
    private static readonly GpuAdapter Radeon = new("AMD Radeon RX 6700 XT", "31.0.0.0", null, @"PCI\VEN_1002&DEV_73DF&SUBSYS_00000000&REV_C1\0");

    // ---- nvidia-smi ----

    [Fact]
    public void Reads_the_real_nvidia_smi_output()
    {
        var gpu = Assert.Single(NvidiaSmi.ParseMemory(Sample("nvidia-smi-rtx3070.xml"))); // DOCTYPE vers un .dtd absent : ignorée
        Assert.Equal(new NvidiaGpuMemory("NVIDIA GeForce RTX 3070", "Ampere", 8192, 8192), gpu);
    }

    // ---- Resizable BAR ----

    [Fact]
    public void Resizable_bar_of_the_dev_machine_is_on()
    {
        var memory = NvidiaSmi.ParseMemory(Sample("nvidia-smi-rtx3070.xml"));
        var result = new ResizableBarCheck(new FakeGpus(Rtx3070), new FakeNvidia(memory)).Run();
        Assert.Equal((DiagnosticStatus.Ok, "Activé."), (result.Status, result.Summary));
        Assert.Contains("8192 Mio", Assert.Single(result.Details));
        Assert.Empty(result.Fixes); // BIOS : jamais de correction
    }

    [Fact]
    public void Disabled_on_a_card_that_supports_it_needs_the_bios()
    {
        var off = new NvidiaGpuMemory("NVIDIA GeForce RTX 3070", "Ampere", 8192, 256);
        var result = new ResizableBarCheck(new FakeGpus(Rtx3070), new FakeNvidia([off])).Run();
        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        Assert.Contains("Above 4G", result.Explanation);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void A_256_mib_window_is_normal_on_older_cards()
    {
        var gtx = new NvidiaGpuMemory("NVIDIA GeForce GTX 1060 6GB", "Pascal", 6144, 256);
        var result = new ResizableBarCheck(new FakeGpus(Rtx3070 with { Name = gtx.Name }), new FakeNvidia([gtx])).Run();
        Assert.Equal((DiagnosticStatus.Info, "Non pris en charge par cette carte."), (result.Status, result.Summary));
    }

    [Fact]
    public void Other_brands_and_a_missing_nvidia_smi_are_not_guessed()
    {
        var amd = new ResizableBarCheck(new FakeGpus(Radeon), new FakeNvidia(null)).Run();
        Assert.Equal((DiagnosticStatus.Info, "État non lu."), (amd.Status, amd.Summary));
        Assert.Contains(amd.Details, d => d.Contains("Smart Access Memory"));

        var noTool = new ResizableBarCheck(new FakeGpus(Rtx3070), new FakeNvidia(null)).Run();
        Assert.Equal(DiagnosticStatus.Info, noTool.Status);
        Assert.Contains(noTool.Details, d => d.Contains("nvidia-smi"));
    }

    // ---- Jeux fenêtrés ----

    private readonly FakeAccessor _registry = new(KnownSettings.RegistryKind);

    private WindowedGamesCheck Windowed => new(new SettingAccessors([_registry]));

    [Fact]
    public void Windowed_games_optimizations_of_the_dev_machine_are_on()
    {
        _registry.Set(KnownSettings.DirectXGlobalSettings, SettingValue.String("AutoHDREnable=1;SwapEffectUpgradeEnable=1;")); // valeur réelle
        var result = Windowed.Run();
        Assert.Equal((DiagnosticStatus.Ok, "Activées."), (result.Status, result.Summary));
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Disabled_optimizations_are_turned_back_on_keeping_the_other_settings()
    {
        _registry.Set(KnownSettings.DirectXGlobalSettings, SettingValue.String("AutoHDREnable=1;SwapEffectUpgradeEnable=0;"));
        var result = Windowed.Run();
        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        var write = Assert.Single(Assert.Single(result.Fixes).Change.Writes);
        Assert.Equal(KnownSettings.DirectXGlobalSettings, write.Target);
        Assert.Equal("AutoHDREnable=1;SwapEffectUpgradeEnable=1;", write.NewValue.Text);
    }

    [Fact]
    public void Absent_value_is_the_undocumented_windows_default()
    {
        var result = Windowed.Run();
        Assert.Equal((DiagnosticStatus.Info, "Réglage par défaut de Windows."), (result.Status, result.Summary));
        Assert.Equal("SwapEffectUpgradeEnable=1;", Assert.Single(Assert.Single(result.Fixes).Change.Writes).NewValue.Text);
    }
}
