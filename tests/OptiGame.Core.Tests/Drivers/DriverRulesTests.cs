using OptiGame.Core.Abstractions;
using OptiGame.Core.Drivers;

namespace OptiGame.Core.Tests.Drivers;

public sealed class DriverRulesTests
{
    // Carte de la machine de dev (WMI Win32_VideoController, 2026-09-30).
    private static readonly GpuAdapter Rtx3070 = new("NVIDIA GeForce RTX 3070", "32.0.15.9186", new DateTime(2026, 1, 20),
        @"PCI\VEN_10DE&DEV_2488&SUBSYS_861719DA&REV_A1\4&1FC990D7&0&0019");

    private static NvidiaDriver Driver(string version) => new("GeForce Game Ready Driver", version, new DateOnly(2026, 9, 22),
        new Uri($"https://us.download.nvidia.com/Windows/{version}/x.exe"), "990.85 MB", null, []);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2488", GpuVendor.Nvidia)]
    [InlineData(@"PCI\VEN_1002&DEV_73BF", GpuVendor.Amd)]
    [InlineData(@"PCI\VEN_8086&DEV_56A0", GpuVendor.Intel)]
    [InlineData(@"ROOT\BasicDisplay", GpuVendor.Other)]
    public void Vendor_comes_from_the_pci_id(string pnp, GpuVendor vendor)
    {
        Assert.Equal(vendor, DriverRules.VendorOf(pnp));
    }

    [Fact]
    public void Newer_nvidia_driver_is_an_available_update()
    {
        var status = DriverRules.Evaluate(Rtx3070, Driver("617.14"));

        Assert.Equal((DriverState.UpdateAvailable, "591.86"), (status.State, status.InstalledVersion));
        Assert.Equal("Nouveau pilote disponible : 617.14 (installé : 591.86).", status.Message);
    }

    [Fact]
    public void Same_or_older_published_driver_means_up_to_date()
    {
        Assert.Equal(DriverState.UpToDate, DriverRules.Evaluate(Rtx3070, Driver("591.86")).State);
        Assert.Equal(DriverState.UpToDate, DriverRules.Evaluate(Rtx3070, Driver("580.10")).State);
    }

    [Fact]
    public void Unknown_when_nothing_can_be_compared()
    {
        Assert.Equal(DriverState.Unknown, DriverRules.Evaluate(Rtx3070, null, "Pas de connexion").State);
        Assert.Equal("Pas de connexion", DriverRules.Evaluate(Rtx3070, null, "Pas de connexion").Message);
        Assert.Equal(DriverState.Unknown, DriverRules.Evaluate(Rtx3070 with { DriverVersion = "?" }, Driver("617.14")).State);

        var amd = DriverRules.Evaluate(new GpuAdapter("AMD Radeon RX 6800", "31.0.21029.1003", null, @"PCI\VEN_1002&DEV_73BF"), null);
        Assert.Equal((GpuVendor.Amd, DriverState.Unknown), (amd.Vendor, amd.State));
        Assert.Contains("Adrenalin", amd.Message);
    }

    // Titres réels de la recherche Windows Update sur la machine de dev.
    private static WindowsUpdateDriver Wu(string title, string driverClass) => new("id", title, driverClass, new DateTime(2026, 7, 22), null, null, false, null);

    [Fact]
    public void Version_is_read_from_the_title()
    {
        Assert.Equal("32.0.16.1088", Wu("NVIDIA Display Driver Update (32.0.16.1088)", "Video").Version);
        Assert.Equal("2.21.4.0", Wu("SAMSUNG Electronics Co., Ltd. USB Driver Update (2.21.4.0)", "OtherHardware").Version);
        Assert.Null(Wu("Pilote sans version", "Video").Version);
    }

    [Fact]
    public void Older_windows_update_nvidia_driver_is_hidden_when_nvidia_has_newer()
    {
        var wuNvidia = Wu("NVIDIA Display Driver Update (32.0.16.1088)", "Video"); // 610.88

        Assert.True(DriverRules.IsSupersededByNvidia(wuNvidia, "617.14"));
        Assert.True(DriverRules.IsSupersededByNvidia(wuNvidia, "610.88"));
        Assert.False(DriverRules.IsSupersededByNvidia(wuNvidia, "605.00")); // Windows Update plus récent : gardé
        Assert.False(DriverRules.IsSupersededByNvidia(wuNvidia, null));     // NVIDIA injoignable : gardé
        Assert.False(DriverRules.IsSupersededByNvidia(Wu("SAMSUNG Electronics Co., Ltd.  Net Driver Update (2.21.4.0)", "Networking"), "617.14"));
        Assert.False(DriverRules.IsSupersededByNvidia(Wu("NVIDIA USB Type-C Driver (1.2.3.4)", "USB"), "617.14"));
    }
}
