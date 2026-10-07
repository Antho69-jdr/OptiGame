using OptiGame.Core.Abstractions;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Diagnostics.Checks;
using OptiGame.Core.Display;

namespace OptiGame.Core.Tests.Display;

public sealed class EdidTests
{
    /// <summary>EDID réels de la machine de dev (2026-10-07), numéros de série effacés.</summary>
    public static byte[] Sample(string model) => Convert.FromHexString(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Display", "Samples", "edid-dev-machine.txt"))
        .Select(l => l.Split(' ')).First(p => p[0] == model)[1]);

    [Theory]
    [InlineData("SAM711A", 3440, 1440, 165)] // 165 Hz annoncés seulement dans l'extension DisplayID (100 Hz dans le bloc de base)
    [InlineData("BNQ78E4", 1920, 1080, 60)]  // plage annoncée jusqu'à 76 Hz, mais 60 Hz à la résolution native
    [InlineData("ACR06A7", 1920, 1080, 144)] // 143,85 Hz
    public void Reads_native_resolution_and_its_highest_refresh(string model, int width, int height, int hz)
    {
        var timings = Edid.Timings(Sample(model));

        Assert.Equal((width, height), Edid.Native(timings));
        Assert.Equal(hz, Edid.MaxRefreshAt(timings, width, height));
    }

    [Fact]
    public void Truncated_or_empty_edid_gives_nothing()
    {
        Assert.Empty(Edid.Timings(new byte[64]));
        Assert.Null(Edid.Native(Edid.Timings(new byte[128])));
    }
}

public sealed class DisplayLinkCheckTests
{
    private sealed class Links(params DisplayLink[] links) : IDisplayLinkInfo
    {
        public IReadOnlyList<DisplayLink> GetLinks() => links;
    }

    private sealed class Gpus(params string[] names) : IGpuInfoProvider
    {
        public IReadOnlyList<GpuAdapter> GetAdapters() => [.. names.Select(n => new GpuAdapter(n, "1.0", null, @"PCI\VEN_0000"))];
    }

    private sealed class Power(bool battery) : IPowerStatusProvider
    {
        public PowerStatus GetStatus() => new(battery, true, null, false);
    }

    private const string Rtx = "NVIDIA GeForce RTX 3070";

    private static DiagnosticResult Run(DisplayLink link, bool laptop = false, params string[] gpus) =>
        new DisplayLinkCheck(new Links(link), new Gpus(gpus.Length == 0 ? [Rtx] : gpus), new Power(laptop)).Run();

    [Fact]
    public void Dev_machine_screens_are_fine()
    {
        var result = new DisplayLinkCheck(new Links(
                new DisplayLink(@"\\.\DISPLAY6", "LC34G55T", Rtx, DisplayConnection.DisplayPort, EdidTests.Sample("SAM711A"), 165, IsPrimary: true),
                new DisplayLink(@"\\.\DISPLAY7", "BenQ GW2470", Rtx, DisplayConnection.Hdmi, EdidTests.Sample("BNQ78E4"), 60)),
            new Gpus(Rtx), new Power(false)).Run();

        Assert.Equal(DiagnosticStatus.Ok, result.Status);
        Assert.Contains(result.Details, d => d.StartsWith("LC34G55T : DisplayPort, carte « NVIDIA GeForce RTX 3070 », annonce 3440×1440 à 165 Hz"));
    }

    [Fact]
    public void A_165_hz_screen_stuck_at_60_hz_over_hdmi_is_flagged()
    {
        var result = Run(new DisplayLink(@"\\.\DISPLAY6", "LC34G55T", Rtx, DisplayConnection.Hdmi, EdidTests.Sample("SAM711A"), 60, IsPrimary: true));

        Assert.Equal(DiagnosticStatus.NeedsAttention, result.Status);
        Assert.Equal("LC34G55T : capable de 165 Hz, limité à 60 Hz.", result.Summary);
        Assert.Contains("HDMI « High Speed »", result.Explanation);
        Assert.Empty(result.Fixes); // conseil matériel : rien à appliquer
    }

    [Fact]
    public void Small_rounding_differences_are_ignored()
    {
        Assert.Equal(DiagnosticStatus.Ok, Run(new DisplayLink("D", "KG241Q", Rtx, DisplayConnection.DisplayPort, EdidTests.Sample("ACR06A7"), 143)).Status);
    }

    [Fact]
    public void Main_screen_on_the_motherboard_of_a_desktop_is_flagged_but_not_on_a_laptop()
    {
        var onMotherboard = new DisplayLink(@"\\.\DISPLAY1", "LC34G55T", "AMD Radeon(TM) Graphics", DisplayConnection.DisplayPort,
            EdidTests.Sample("SAM711A"), 165, IsPrimary: true);

        var desktop = Run(onMotherboard, false, "AMD Radeon(TM) Graphics", Rtx);
        Assert.Equal(DiagnosticStatus.NeedsAttention, desktop.Status);
        Assert.Equal("LC34G55T : branché sur la carte mère.", desktop.Summary);
        Assert.Contains("rebranchez-le sur une sortie de la carte graphique « NVIDIA GeForce RTX 3070 »", desktop.Explanation);

        Assert.Equal(DiagnosticStatus.Ok, Run(onMotherboard, true, "AMD Radeon(TM) Graphics", Rtx).Status);         // portable
        Assert.Equal(DiagnosticStatus.Ok, Run(onMotherboard with { IsPrimary = false }, false, "AMD Radeon(TM) Graphics", Rtx).Status); // écran secondaire
        Assert.Equal(DiagnosticStatus.Ok, Run(onMotherboard, false, "AMD Radeon(TM) Graphics").Status);              // pas de carte dédiée
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3070", true)]
    [InlineData("AMD Radeon RX 7800 XT", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", true)]
    [InlineData("AMD Radeon(TM) Graphics", false)]
    [InlineData("Intel(R) UHD Graphics 770", false)]
    public void Tells_dedicated_from_integrated_cards(string name, bool dedicated) => Assert.Equal(dedicated, DisplayLinkCheck.IsDedicated(name));
}
