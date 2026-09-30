using OptiGame.Core.Drivers;

namespace OptiGame.Core.Tests.Drivers;

public sealed class DriverInstallTests
{
    [Theory]
    // Signataire réel de l'installeur 617.14 (lu le 2026-09-30).
    [InlineData("CN=NVIDIA Corporation, OU=2008B9F, O=NVIDIA Corporation, L=Santa Clara, S=California, C=US", true)]
    [InlineData("CN=NVIDIA Corporation, O=NVIDIA Corporation, C=US", true)]
    [InlineData("CN=Intel Corporation, O=Intel Corporation, S=California, C=US", false)]
    [InlineData("CN=NVIDIA Corporation Ltd, O=NVIDIA Corporation, C=US", false)]
    [InlineData("CN=NVIDIA Corporation, O=\"Evil, Inc.\", C=US", false)]
    [InlineData("O=NVIDIA Corporation, CN=Someone Else", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_nvidia_corporation_is_accepted_as_signer(string? subject, bool expected)
    {
        Assert.Equal(expected, InstallerSignature.IsNvidia(subject));
    }

    [Theory]
    [InlineData("https://us.download.nvidia.com/Windows/617.14/617.14-desktop-win10-win11-64bit-international-dch-whql.exe",
        "617.14-desktop-win10-win11-64bit-international-dch-whql.exe")]
    [InlineData("https://us.download.nvidia.com/Windows/617.14/..%5C..%5Cevil.exe", null)]
    [InlineData("https://us.download.nvidia.com/Windows/617.14/driver.zip", null)]
    [InlineData("https://evil.example/driver.exe", null)]
    public void Installer_file_name_is_safe_and_official(string url, string? expected)
    {
        Assert.Equal(expected, InstallerFiles.FileNameFor(new Uri(url)));
    }

    [Fact]
    public void Nvidia_plan_says_what_why_and_that_it_cannot_be_undone()
    {
        var latest = new NvidiaDriver("GeForce Game Ready Driver", "617.14", new DateOnly(2026, 9, 22),
            new Uri("https://us.download.nvidia.com/Windows/617.14/x.exe"), "990.85 MB", null, []);
        var status = new GpuDriverStatus("NVIDIA GeForce RTX 3070", GpuVendor.Nvidia, "591.86", latest, DriverState.UpdateAvailable, "");

        var plan = DriverInstallPlans.ForNvidia(status, latest, RestorePointAvailability.ProtectionDisabled);

        Assert.Equal("Installer le pilote NVIDIA 617.14", plan.Title);
        Assert.Contains("591.86 → 617.14", plan.What);
        Assert.Contains("us.download.nvidia.com (990,85 Mo)", plan.What);
        Assert.Contains("signature", plan.Why);
        Assert.Contains("ne peut pas annuler", plan.NotReversible);
        Assert.Contains("Restaurer le pilote", plan.Rollback);
        Assert.True(plan.MayRequireReboot);
        Assert.Contains("désactivée", DriverInstallPlans.RestorePointText(plan.RestorePoint));
    }

    [Fact]
    public void Restore_point_texts_cover_the_three_states()
    {
        Assert.Contains("recommandé", DriverInstallPlans.RestorePointText(RestorePointAvailability.Available));
        Assert.Contains("désactivée", DriverInstallPlans.RestorePointText(RestorePointAvailability.ProtectionDisabled));
        Assert.Contains("Impossible de vérifier", DriverInstallPlans.RestorePointText(RestorePointAvailability.Unknown));
    }

    [Fact]
    public void Windows_update_plan_lists_the_selected_drivers()
    {
        WindowsUpdateDriver Wu(string title, bool reboot) => new(Guid.NewGuid().ToString(), title, "OtherHardware", null, null, null, reboot, null);

        var one = DriverInstallPlans.ForWindowsUpdate([Wu("SAMSUNG USB Driver Update (2.21.4.0)", false)], RestorePointAvailability.Available);
        var two = DriverInstallPlans.ForWindowsUpdate([Wu("A (1.0.0.0)", false), Wu("B (2.0.0.0)", true)], RestorePointAvailability.Available);

        Assert.Equal(("Installer 1 pilote de Windows Update", false), (one.Title, one.MayRequireReboot));
        Assert.Equal(("Installer 2 pilotes de Windows Update", "A (1.0.0.0)\nB (2.0.0.0)", true), (two.Title, two.What, two.MayRequireReboot));
    }

    [Theory]
    [InlineData(2, true, "installé")]
    [InlineData(3, true, "installé avec des avertissements")]
    [InlineData(4, false, "échec")]
    [InlineData(5, false, "annulé")]
    [InlineData(0, false, "non installé")]
    public void Windows_update_result_codes_are_explained(int code, bool succeeded, string text)
    {
        var result = new WindowsUpdateInstallResult("x", code);
        Assert.Equal((succeeded, text), (result.Succeeded, result.Describe()));
    }
}
