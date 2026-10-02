using OptiGame.Core.Drivers;

namespace OptiGame.Core.Tests.Drivers;

public sealed class AmdChipsetTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Drivers", "Samples", name));

    [Theory]
    [InlineData("B450M MORTAR MAX (MS-7B89)", "am4", "b450")]  // carte mère de la machine de dev
    [InlineData("ROG STRIX X570-E GAMING", "am4", "x570")]
    [InlineData("MAG B650M MORTAR WIFI", "am5", "b650")]     // le M = micro-ATX
    [InlineData("X670E AORUS MASTER", "am5", "x670e")]
    [InlineData("PRIME X870E-P WIFI", "am5", "x870e")]
    [InlineData("A520M-A PRO", "am4", "a520")]
    public void Recognizes_the_chipset_from_the_motherboard_name(string product, string socket, string chipset)
    {
        Assert.Equal(new AmdBoard(socket, chipset), AmdChipset.BoardFromProduct(product));
    }

    [Theory]
    [InlineData("Z790 AORUS ELITE")] // Intel
    [InlineData("MS-7B89")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_boards_are_not_guessed(string? product)
    {
        Assert.Null(AmdChipset.BoardFromProduct(product));
    }

    [Fact]
    public void Support_page_address_matches_the_verified_one()
    {
        Assert.Equal("https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/b450.html",
            AmdChipset.SupportPage(new AmdBoard("am4", "b450")).ToString());
    }

    [Fact]
    public void Reads_the_real_amd_page()
    {
        var release = AmdChipset.ParsePage(Sample("amd-chipset-b450-excerpt.html"));

        Assert.NotNull(release);
        Assert.Equal("8.08.12.551", release.Version);
        Assert.Equal("79 MB", release.SizeText);
        Assert.Equal(new DateOnly(2026, 8, 14), release.ReleaseDate);
        Assert.Equal("https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe", release.DownloadUrl.ToString());
        Assert.Equal("https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-8-08-12-551.html", release.ReleaseNotes?.ToString());
        Assert.True(OfficialInstallers.IsOfficialDownload(InstallerVendor.Amd, release.DownloadUrl));
    }

    [Fact]
    public void Ignores_the_windows_7_package_and_other_drivers()
    {
        const string html = """
            <article class="container-fluid driver-download-details"><h4>AMD RAID Installer (SATA, NVMe RAID)</h4>
            <strong>Revision Number</strong><p>2.20.19.037</p><a href="https://drivers.amd.com/drivers/amd_raid_software_2.20.19.037.exe">x</a></article>
            <article class="container-fluid driver-download-details"><h4>AMD Chipset Drivers</h4>
            <strong>Revision Number</strong><p>2.17.25.506</p><a href="https://drivers.amd.com/drivers/AMD_Chipset_Software_Win7_2.17.25.506.exe">x</a></article>
            """;
        Assert.Null(AmdChipset.ParsePage(html));
        Assert.Null(AmdChipset.ParsePage("<html>page sans pilotes</html>"));
    }

    private static readonly AmdChipsetRelease Latest = new("8.08.12.551", "79 MB", new DateOnly(2026, 8, 14),
        new Uri("https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe"), null);

    // Pilotes AMD réels de la machine de dev (2026-10-02) : paquet non installé, pilotes séparés.
    private static readonly AmdDriverInfo[] DevDrivers =
    [
        new("AMD GPIO Controller", "2.0.1.0", new DateOnly(2020, 9, 3)),
        new("AMD GPIO Controller", "2.2.0.133", new DateOnly(2024, 5, 4)),
        new("AMD SMBus", "5.12.0.38", new DateOnly(2022, 9, 15)),
        new("AMD PSP 11.0 Device", "4.13.0.0", new DateOnly(2020, 6, 3)),
        new("AMD PCI", "1.0.0.90", new DateOnly(2022, 10, 27)),
    ];

    private static readonly Uri Page = new("https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/b450.html");

    [Fact]
    public void Separate_old_drivers_without_package_mean_an_update()
    {
        var status = AmdChipset.Evaluate("Ryzen 5 3600 · B450", null, DevDrivers, Latest, Page);

        Assert.Equal(DriverState.UpdateAvailable, status.State);
        Assert.Equal("AMD Chipset Software non installé ; 5 pilote(s) AMD séparé(s), le plus ancien (AMD PSP 11.0 Device) du 03/06/2020.", status.InstalledText);
        Assert.Equal("Nouveau logiciel de chipset disponible : 8.08.12.551.", status.Message);
    }

    [Theory]
    [InlineData("7.06.02.123", DriverState.UpdateAvailable)]
    [InlineData("8.08.12.551", DriverState.UpToDate)]
    [InlineData("8.09.01.100", DriverState.UpToDate)]
    public void Installed_package_is_compared_by_version(string installed, DriverState expected)
    {
        var status = AmdChipset.Evaluate("x", installed, DevDrivers, Latest, Page);
        Assert.Equal(expected, status.State);
        Assert.Equal($"AMD Chipset Software {installed} installé.", status.InstalledText);
    }

    [Fact]
    public void Drivers_newer_than_the_release_mean_up_to_date()
    {
        var recent = new[] { new AmdDriverInfo("AMD PSP 11.0 Device", "5.40.0.0", new DateOnly(2026, 9, 1)) };
        Assert.Equal(DriverState.UpToDate, AmdChipset.Evaluate("x", null, recent, Latest, Page).State);
    }

    [Fact]
    public void Unknown_when_amd_cannot_be_read()
    {
        var status = AmdChipset.Evaluate("x", null, DevDrivers, null, Page, "Site d'AMD injoignable");
        Assert.Equal((DriverState.Unknown, "Site d'AMD injoignable"), (status.State, status.Message));
    }

    [Theory]
    // Signataire réel de l'installeur 8.08.12.551 (lu le 2026-10-02).
    [InlineData("CN=Advanced Micro Devices, O=Advanced Micro Devices, S=California, C=US", true)]
    [InlineData("CN=Advanced Micro Devices Inc, O=Advanced Micro Devices, C=US", false)]
    [InlineData("CN=NVIDIA Corporation, O=NVIDIA Corporation, C=US", false)]
    public void Only_advanced_micro_devices_signs_amd_installers(string subject, bool expected)
    {
        Assert.Equal(expected, OfficialInstallers.IsExpectedSigner(InstallerVendor.Amd, subject));
    }

    [Theory]
    [InlineData("https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe", "AMD_Chipset_Software_8.08.12.551.exe")]
    [InlineData("http://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe", null)]
    [InlineData("https://drivers.amd.com.evil.example/x.exe", null)]
    [InlineData("https://us.download.nvidia.com/Windows/617.14/x.exe", null)] // serveur d'un autre fabricant
    public void Amd_installers_come_only_from_drivers_amd_com(string url, string? expected)
    {
        Assert.Equal(expected, OfficialInstallers.FileNameFor(InstallerVendor.Amd, new Uri(url)));
    }

    [Fact]
    public void Amd_downloads_announce_amd_com_as_origin()
    {
        Assert.Equal("https://www.amd.com/", OfficialInstallers.Referer(InstallerVendor.Amd)?.ToString());
        Assert.Null(OfficialInstallers.Referer(InstallerVendor.Nvidia));
    }
}

public sealed class AmdChipsetInstallPlanTests
{
    [Fact]
    public void Plan_says_what_why_and_that_it_cannot_be_undone()
    {
        var latest = new AmdChipsetRelease("8.08.12.551", "79 MB", new DateOnly(2026, 8, 14),
            new Uri("https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe"), null);
        var status = new ChipsetDriverStatus("AMD Ryzen 5 3600 · B450M MORTAR MAX (chipset B450)", "AMD Chipset Software non installé.",
            latest, DriverState.UpdateAvailable, "", null);

        var plan = DriverInstallPlans.ForAmdChipset(status, latest, RestorePointAvailability.Available);

        Assert.Equal("Installer le logiciel de chipset AMD 8.08.12.551", plan.Title);
        Assert.Contains("→ AMD Chipset Software 8.08.12.551 du 14/08/2026", plan.What);
        Assert.Contains("drivers.amd.com (79 Mo)", plan.What);
        Assert.Contains("Advanced Micro Devices", plan.Why);
        Assert.Contains("ne peut pas annuler", plan.NotReversible);
        Assert.Contains("Paramètres > Applications", plan.Rollback);
    }
}
