using OptiGame.Core.Measurement;
using OptiGame.Core.Rating;

namespace OptiGame.Core.Tests.Rating;

public sealed class GpuPerformanceTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 3070", "RTX 3070", 235, "NVIDIA")]   // carte de la machine de dev (WMI)
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", "RTX 4070 Ti Super", 380, "NVIDIA")]
    [InlineData("AMD Radeon RX 6700 XT", "RX 6700 XT", 205, "AMD")]
    [InlineData("AMD Radeon RX 7900 XTX", "RX 7900 XTX", 420, "AMD")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", "Arc A770", 150, "Intel")]
    [InlineData("AMD Radeon R9 380X", "R9 380X", 75, "AMD")]
    public void Identifies_installed_cards(string name, string model, int index, string vendor)
    {
        Assert.Equal(new GpuPerformance.Match(model, index, vendor), GpuPerformance.Identify(name));
    }

    [Theory]
    [InlineData("Microsoft Basic Display Adapter")]
    [InlineData("NVIDIA GeForce RTX 9999")]
    public void Unknown_cards_are_not_guessed(string name)
    {
        Assert.Null(GpuPerformance.Identify(name));
    }

    [Fact]
    public void Finds_alternatives_and_series_in_requirement_texts()
    {
        // Textes réels (Steam, 2026-10-02).
        Assert.Equal(["GTX 960", "R7 370"], GpuPerformance.Find("NVIDIA GeForce GTX 960 2GB / AMD Radeon R7 370 2GB").Select(m => m.Model));
        Assert.Equal(["GTX 1060", "GTX 1650", "R9 380", "RX 6400", "Arc A770"],
            GpuPerformance.Find("NVIDIA® GeForce® GTX 1060/ GeForce® GTX 1650 or AMD R9 380/AMD RX 6400 or Intel® Arc™ A770").Select(m => m.Model));
        Assert.Equal(["GTX 600 (série)", "HD 7000 (série)"], GpuPerformance.Find("NVIDIA® GeForce® GTX 600 series, AMD Radeon™ HD 7000 series").Select(m => m.Model));
        Assert.Equal(["Iris Xe"], GpuPerformance.Find("Intel Iris Xe Graphics or equivalent").Select(m => m.Model));
        Assert.Equal(["RTX 3060", "RX 6700 XT"], GpuPerformance.Find("NVIDIA RTX 3060 (12 GB VRAM), AMD RX 6700 XT (12 GB VRAM)").Select(m => m.Model));
    }
}

public sealed class GameRatingTests
{
    private static string Sample(string appId) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Rating", "Samples", $"steam-appdetails-{appId}.json"));

    private static SystemRequirements Requirements(string appId) => SteamRequirements.ParseAppDetails(Sample(appId), appId)!;

    // Machine de dev : RTX 3070, 32 Go, écran principal 3440×1440 à 165 Hz.
    private static readonly PcSpecs Dev = new("NVIDIA GeForce RTX 3070", GpuPerformance.Identify("NVIDIA GeForce RTX 3070"), 32, 3440, 1440, 165);

    [Fact]
    public void Reads_the_real_steam_requirements()
    {
        var pubg = Requirements("578080");
        Assert.Equal((8, 16), (pubg.Minimum?.MemoryGb, pubg.Recommended?.MemoryGb));
        Assert.Equal("NVIDIA GeForce GTX 960 2GB / AMD Radeon R7 370 2GB", pubg.Minimum?.GraphicsText);
        Assert.Equal(["GTX 1060", "RX 580"], pubg.Recommended!.Gpus.Select(g => g.Model));

        var voidCrew = Requirements("1063420");
        Assert.Equal(["RX 5700", "GTX 1080"], voidCrew.Recommended!.Gpus.Select(g => g.Model));
    }

    [Theory]
    [InlineData("578080", GraphicsPreset.High, 77)]     // PUBG : recommandé GTX 1060
    [InlineData("2357570", GraphicsPreset.Ultra, 88)]   // Overwatch : recommandé GTX 1650 (la plus faible NVIDIA citée)
    [InlineData("387990", GraphicsPreset.Medium, 59)]   // Scrap Mechanic : recommandé RTX 3060
    [InlineData("1063420", GraphicsPreset.Medium, 60)]  // Void Crew : recommandé GTX 1080
    public void Estimates_the_dev_pc_on_its_real_games(string appId, GraphicsPreset preset, int score)
    {
        var estimate = GameRatings.EstimateFrom(Dev, Requirements(appId));

        Assert.NotNull(estimate);
        Assert.Equal((preset, score, false), (estimate.Preset, estimate.Score, estimate.BelowMinimum));
    }

    [Fact]
    public void Resolution_matters_the_same_card_in_1080p_gets_higher_settings()
    {
        var fullHd = Dev with { Width = 1920, Height = 1080 };
        Assert.Equal(GraphicsPreset.Ultra, GameRatings.EstimateFrom(fullHd, Requirements("578080"))!.Preset);
    }

    [Fact]
    public void Weak_card_or_missing_memory_is_below_minimum()
    {
        var weak = Dev with { Gpu = GpuPerformance.Identify("NVIDIA GeForce GTX 750 Ti") };
        var estimate = GameRatings.EstimateFrom(weak, Requirements("578080"))!;
        Assert.True(estimate.BelowMinimum);
        Assert.InRange(estimate.Score, 15, 35);

        var lowMemory = GameRatings.EstimateFrom(Dev with { MemoryGb = 4 }, Requirements("578080"))!;
        Assert.True(lowMemory.BelowMinimum);

        var midMemory = GameRatings.EstimateFrom(Dev with { MemoryGb = 8, Width = 1920, Height = 1080 }, Requirements("578080"))!;
        Assert.Equal(GraphicsPreset.Medium, midMemory.Preset); // 8 Go < 16 Go recommandés : pas au-delà de Moyen
    }

    [Fact]
    public void No_estimate_without_a_recognized_card()
    {
        Assert.Null(GameRatings.EstimateFrom(Dev with { Gpu = null }, Requirements("578080")));
        Assert.Null(GameRatings.EstimateFrom(Dev, new SystemRequirements(new RequirementLevel([], 8, "512 MB VRAM"), null)));
    }

    private static FrameStats Stats(double avg, double low, int frames = 5000) => new(frames, 60, avg, low, low * 0.8, 1000 / avg, 1000 / low, 1000 / low * 2);

    [Theory]
    [InlineData(144, 120, 100)]
    [InlineData(98, 71, 80)]
    [InlineData(60, 45, 57)]
    [InlineData(30, 20, 34)]
    public void Measured_score_reflects_smoothness(double avg, double low, int score)
    {
        Assert.Equal(score, GameRatings.MeasureFrom([Stats(avg, low)], 165)!.Score);
    }

    [Fact]
    public void Measured_score_uses_the_median_of_the_last_five_reliable_captures()
    {
        var captures = new[] { Stats(30, 20), Stats(98, 71), Stats(144, 120), Stats(60, 45), Stats(10, 5, frames: 50), Stats(100, 80), Stats(20, 10) };
        var measured = GameRatings.MeasureFrom(captures, 165)!;

        Assert.Equal(5, measured.Captures); // la capture de 50 images est ignorée, la 7e est trop ancienne
        Assert.Equal(80, measured.Score);
        Assert.Null(GameRatings.MeasureFrom([Stats(100, 80, frames: 20)], 165));
    }

    [Fact]
    public void Measurement_replaces_the_estimate_and_adjusts_the_setting()
    {
        var estimate = GameRatings.EstimateFrom(Dev, Requirements("578080")); // Élevé

        var headroom = GameRatings.Combine(estimate, GameRatings.MeasureFrom([Stats(200, 140)], 165))!;
        Assert.Equal((RatingSource.Measured, GraphicsPreset.Ultra, 100, "Très fluide"), (headroom.Source, headroom.Preset, headroom.Score, headroom.Headline));

        var struggling = GameRatings.Combine(estimate, GameRatings.MeasureFrom([Stats(40, 25)], 165))!;
        Assert.Equal((GraphicsPreset.Medium, "Peu fluide"), (struggling.Preset, struggling.Headline));

        var fine = GameRatings.Combine(estimate, GameRatings.MeasureFrom([Stats(98, 71)], 165))!;
        Assert.Equal((GraphicsPreset.High, "Fluide"), (fine.Preset, fine.Headline));

        var onlyEstimate = GameRatings.Combine(estimate, null)!;
        Assert.Equal((RatingSource.Estimated, 77, "Réglages élevé conseillés"), (onlyEstimate.Source, onlyEstimate.Score, onlyEstimate.Headline));

        var measuredOnly = GameRatings.Combine(null, GameRatings.MeasureFrom([Stats(98, 71)], 165))!;
        Assert.Null(measuredOnly.Preset); // jeu hors Steam : pas de configuration requise, conseil relatif seulement
        Assert.Null(GameRatings.Combine(null, null));
    }
}
