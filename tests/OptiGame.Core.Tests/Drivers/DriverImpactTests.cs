using OptiGame.Core.Abstractions;
using OptiGame.Core.Drivers;
using OptiGame.Core.Measurement;
using OptiGame.Core.Rating;

namespace OptiGame.Core.Tests.Drivers;

public sealed class DriverImpactTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 20, 0, 0, TimeSpan.FromHours(2));

    private static CaptureRecord Capture(string exe, double fps, string? driver, int day, GraphicsPreset? preset = GraphicsPreset.High, int frames = 5000) => new()
    {
        Label = "Automatique",
        ProcessName = exe,
        CsvFile = "x.csv",
        CapturedAt = Start.AddDays(day),
        Stats = new FrameStats(frames, 60, fps, fps * 0.7, fps * 0.5, 1000 / fps, 1400 / fps, 2000 / fps),
        Preset = preset,
        GpuDriver = driver,
    };

    private static string? Game(string exe) => exe switch { "Overwatch.exe" => "Overwatch", "Void Crew.exe" => "Void Crew", _ => null };

    [Fact]
    public void Compares_each_game_before_and_after_the_last_driver_change()
    {
        var captures = new[]
        {
            Capture("Overwatch.exe", 160, "616.56", 0), Capture("Overwatch.exe", 164, "616.56", 1), Capture("Overwatch.exe", 162, "616.56", 2),
            Capture("Overwatch.exe", 150, "617.42", 3), Capture("Overwatch.exe", 152, "617.42", 4),
            Capture("Void Crew.exe", 100, "616.56", 1), Capture("Void Crew.exe", 110, "617.42", 4),
            Capture("Void Crew.exe", 80, "617.42", 5, GraphicsPreset.Ultra), // plus récente, autre réglage : c'est elle qui fixe le réglage
            Capture("notepad.exe", 60, "616.56", 1), Capture("notepad.exe", 60, "617.42", 4), // pas un jeu de Mes jeux
            Capture("Overwatch.exe", 999, null, 0),                                            // capture ancienne, sans pilote noté
        };

        var report = DriverImpacts.Compare(captures, "617.42", Game)!;

        Assert.Equal(("616.56", "617.42"), (report.PreviousDriver, report.CurrentDriver));
        var overwatch = Assert.Single(report.Games); // Void Crew : aucune mesure en Ultra sous l'ancien pilote
        Assert.Equal(("Overwatch", 162, 151, 3, 2), (overwatch.Game, overwatch.FpsBefore, overwatch.FpsAfter, overwatch.CapturesBefore, overwatch.CapturesAfter));
        Assert.True(report.HasRegression); // −6,8 %
        Assert.Equal("Overwatch : 162 → 151 FPS (−7 %)", DriverImpacts.Describe(overwatch));
    }

    [Fact]
    public void Nothing_to_compare_without_measures_under_two_drivers()
    {
        Assert.Null(DriverImpacts.Compare([Capture("Overwatch.exe", 160, "617.42", 0)], "617.42", Game));
        Assert.Null(DriverImpacts.Compare([Capture("Overwatch.exe", 160, null, 0), Capture("Overwatch.exe", 150, "617.42", 1)], "617.42", Game));
        // Pilote précédent sans mesure au même réglage
        Assert.Null(DriverImpacts.Compare([Capture("Overwatch.exe", 160, "616.56", 0, GraphicsPreset.Low), Capture("Overwatch.exe", 150, "617.42", 1)], "617.42", Game));
    }

    [Fact]
    public void Small_changes_are_not_a_regression()
    {
        var report = DriverImpacts.Compare([Capture("Overwatch.exe", 160, "616.56", 0), Capture("Overwatch.exe", 156, "617.42", 1)], "617.42", Game)!;
        Assert.False(report.HasRegression); // −2,5 %
    }

    [Fact]
    public void Driver_label_of_the_gaming_card()
    {
        // Machine de dev : RTX 3070, pilote Windows 32.0.16.1742 = NVIDIA 617.42.
        var nvidia = new GpuAdapter("NVIDIA GeForce RTX 3070", "32.0.16.1742", null, @"PCI\VEN_10DE&DEV_2484&SUBSYS_00000000");
        var intel = new GpuAdapter("Intel(R) UHD Graphics", "31.0.101.2125", null, @"PCI\VEN_8086&DEV_4680");
        var basic = new GpuAdapter("Microsoft Basic Display Adapter", "10.0.1", null, @"ROOT\BasicDisplay");

        Assert.Equal("617.42", DriverImpacts.CurrentDriver([intel, nvidia, basic]));
        Assert.Equal("31.0.101.2125", DriverImpacts.CurrentDriver([intel, basic]));
        Assert.Null(DriverImpacts.CurrentDriver([basic]));
    }
}
