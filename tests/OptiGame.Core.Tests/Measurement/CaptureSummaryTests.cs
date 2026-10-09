using OptiGame.Core.Measurement;
using OptiGame.Core.Rating;

namespace OptiGame.Core.Tests.Measurement;

public sealed class CaptureSummaryTests
{
    private static CaptureRecord Capture(double fps, double low, double p99, int frames = 5000) => new()
    {
        Label = "Automatique",
        ProcessName = "portal2.exe",
        CsvFile = @"C:\Users\x\AppData\Local\OptiGame\captures\a.csv",
        Stats = new FrameStats(frames, 60, fps, low, low * 0.8, 1000 / fps, p99, p99 * 1.5),
        Preset = GraphicsPreset.Ultra,
        GpuDriver = "617.42",
    };

    [Fact]
    public void One_measure_in_plain_french_without_any_path()
    {
        var text = CaptureSummary.Of(Capture(288.4, 193.3, 5.2), "Portal 2", "NVIDIA GeForce RTX 3070", new DateTime(2026, 10, 4, 15, 21, 0));

        Assert.Equal("Portal 2 — 288,4 FPS moyens · 1\u202F% les plus lents : 193,3 FPS · temps d'image P99 : 5,2 ms · réglage Ultra · " +
                     "NVIDIA GeForce RTX 3070 (pilote 617.42) · mesuré le 4 oct. 2026 avec OptiGame", text);
        Assert.DoesNotContain(@"C:\", text);
    }

    [Fact]
    public void A_short_measure_says_it_is_unreliable_and_the_gpu_is_optional()
    {
        var text = CaptureSummary.Of(Capture(60, 50, 20, frames: 10), "Portal 2", null, new DateTime(2026, 10, 4));
        Assert.EndsWith("(mesure courte : peu fiable)", text);
        Assert.DoesNotContain("pilote", text);
    }

    [Fact]
    public void Two_measures_show_before_after_and_the_change()
    {
        var text = CaptureSummary.Compare(Capture(250, 150, 8), Capture(287.5, 165, 6), "Portal 2", "Portal 2", "NVIDIA GeForce RTX 3070");
        Assert.StartsWith("Portal 2 — avant / après : FPS moyens 250,0 → 287,5 (+15\u202F%)", text);
        Assert.Contains("P99 8,0 → 6,0 ms (−25\u202F%)", text);
    }

    [Fact]
    public void Two_different_games_are_both_named()
    {
        var text = CaptureSummary.Compare(Capture(131.4, 75.4, 11), Capture(60, 57.7, 17.1), "ARC Raiders", "Overwatch", null);
        Assert.StartsWith("ARC Raiders → Overwatch : FPS moyens 131,4 → 60,0", text); // jamais « avant / après » d'un seul jeu
    }
}
