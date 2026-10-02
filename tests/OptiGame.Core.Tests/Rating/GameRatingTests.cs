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

    private static GameRatings.MeasuredCapture Capture(double avg, double low, int frames = 5000, FrameLoad? load = null, GraphicsPreset? preset = null) =>
        new(Stats(avg, low, frames), load, preset);

    // Charges relevées sur de vraies captures PresentMon (2026-10-02).
    private static readonly FrameLoad OverwatchCapped = new(0.76, 0.43);
    private static readonly FrameLoad VoidCrewGpuBound = new(0.98, 0.02);

    [Theory]
    [InlineData(144, 120, 100)]
    [InlineData(98, 71, 80)]
    [InlineData(60, 45, 57)]
    [InlineData(30, 20, 34)]
    public void Measured_score_reflects_smoothness(double avg, double low, int score)
    {
        Assert.Equal(score, GameRatings.MeasureFrom([Capture(avg, low)], 165)!.Score);
    }

    [Fact]
    public void Measured_score_uses_the_median_of_the_last_five_reliable_captures()
    {
        var captures = new[] { Capture(30, 20), Capture(98, 71), Capture(144, 120), Capture(60, 45), Capture(10, 5, frames: 50), Capture(100, 80), Capture(20, 10) };
        var measured = GameRatings.MeasureFrom(captures, 165)!;

        Assert.Equal(5, measured.Captures); // la capture de 50 images est ignorée, la 7e est trop ancienne
        Assert.Equal(80, measured.Score);
        Assert.Null(GameRatings.MeasureFrom([Capture(100, 80, frames: 20)], 165));
    }

    [Fact]
    public void Reads_the_load_columns_of_a_real_PresentMon_capture()
    {
        // En-tête et 2 lignes d'une vraie capture d'Overwatch (PresentMon 2.6.0, --v2_metrics), répétées.
        const string header = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,CPUStartTime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime,AnimationError,AnimationTime,MsFlipDelay,AllInputToPhotonLatency,ClickToPhotonLatency";
        const string a = "Overwatch.exe,2416,0x1CB732B1060,DXGI,0,0,1,Hardware Composed: Independent Flip,5004.7366,6.2291,3.4179,2.8112,1.3188,5.8195,4.5946,1.2249,11.9221,6.1469,-0.0716,5004.7366,NA,16.4738,NA";
        const string b = "Overwatch.exe,2416,0x1CB732B1060,DXGI,0,0,1,Hardware Composed: Independent Flip,5010.9657,6.0092,5.4723,0.5369,1.1948,5.7305,4.4662,1.2643,11.8399,5.9703,0.0822,5010.9657,NA,NA,NA";
        var csv = header + "\n" + string.Join("\n", Enumerable.Repeat(a + "\n" + b, 60));
        var frames = PresentMonCsv.Parse(new StringReader(csv));

        Assert.Equal((2.8112, 4.5946), (frames[0].CpuWait, frames[0].GpuBusy));
        var load = FrameLoad.Compute(frames)!;
        Assert.Equal(0.74, load.GpuBusy, 2);   // (4,5946 + 4,4662) / (6,2291 + 6,0092)
        Assert.Equal(0.27, load.CpuWait, 2);
        Assert.Null(FrameLoad.Compute(frames.Take(50).ToList())); // trop peu d'images
        Assert.Null(FrameLoad.Compute(frames.Select(f => f with { GpuBusy = null }).ToList())); // CSV 1.x
    }

    [Theory]
    [InlineData(0.98, 0.02, 102, Bottleneck.Gpu)]       // Void Crew
    [InlineData(0.76, 0.43, 164, Bottleneck.FrameCap)]  // Overwatch, limiteur de FPS
    [InlineData(0.82, 0.64, 165, Bottleneck.FrameCap)]  // Overwatch en V-Sync (mesure automatique réelle)
    [InlineData(0.60, 0.01, 163, Bottleneck.FrameCap)]  // FPS collés à la fréquence de l'écran sans attente visible
    [InlineData(0.55, 0.02, 90, Bottleneck.Cpu)]
    public void Finds_what_limits_the_frame_rate(double gpuBusy, double cpuWait, double fps, Bottleneck expected)
    {
        Assert.Equal(expected, GameRatings.Classify(new FrameLoad(gpuBusy, cpuWait), fps, 165));
    }

    [Fact]
    public void Advice_starts_from_the_setting_the_user_plays_with()
    {
        var estimate = GameRatings.EstimateFrom(Dev, Requirements("578080")); // Élevé estimé : ignoré une fois mesuré

        // Bas à 200 FPS (carte graphique à fond) : monter d'un cran, depuis Bas.
        var low = GameRatings.Combine(estimate, GameRatings.MeasureFrom([Capture(200, 140, load: VoidCrewGpuBound, preset: GraphicsPreset.Low)], 165))!;
        Assert.Equal((RatingSource.Measured, GraphicsPreset.Medium, 100, "Très fluide"), (low.Source, low.Preset, low.Score, low.Headline));

        // Ultra à 40 FPS, carte graphique à fond : baisser d'un cran.
        var ultra = GameRatings.Combine(estimate, GameRatings.MeasureFrom([Capture(40, 25, load: VoidCrewGpuBound, preset: GraphicsPreset.Ultra)], 165))!;
        Assert.Equal((GraphicsPreset.High, "Peu fluide"), (ultra.Preset, ultra.Headline));
        Assert.Contains("98 %", ultra.Advice);

        // Bas à 40 FPS, carte graphique à fond : impossible de baisser encore.
        var floor = GameRatings.Combine(null, GameRatings.MeasureFrom([Capture(40, 25, load: VoidCrewGpuBound, preset: GraphicsPreset.Low)], 165))!;
        Assert.Equal(GraphicsPreset.Low, floor.Preset);
        Assert.Contains("upscaling", floor.Advice);

        // Processeur limitant : baisser les graphismes ne servirait à rien, le réglage est gardé.
        var cpu = GameRatings.Combine(null, GameRatings.MeasureFrom([Capture(60, 40, load: new FrameLoad(0.55, 0.02), preset: GraphicsPreset.High)], 165))!;
        Assert.Equal(GraphicsPreset.High, cpu.Preset);
        Assert.Contains("processeur", cpu.Advice);

        // Overwatch : plafonné à 164 FPS, carte graphique à 76 % : réglage gardé.
        var capped = GameRatings.Combine(null, GameRatings.MeasureFrom([Capture(164, 117, load: OverwatchCapped, preset: GraphicsPreset.Ultra)], 164))!;
        Assert.Equal((GraphicsPreset.Ultra, 100), (capped.Preset, capped.Score));
        Assert.Contains(capped.Details, d => d.Contains("plafond de FPS"));
    }

    [Fact]
    public void Without_the_users_setting_the_advice_is_only_relative()
    {
        var estimate = GameRatings.EstimateFrom(Dev, Requirements("578080"));
        var rating = GameRatings.Combine(estimate, GameRatings.MeasureFrom([Capture(200, 140, load: VoidCrewGpuBound)], 165))!;

        Assert.Null(rating.Preset); // OptiGame ne peut pas savoir si ces 200 FPS sont en Bas ou en Ultra
        Assert.Contains("monter les réglages d'un cran", rating.Advice);
        Assert.Contains(rating.Details, d => d.StartsWith("Indiquez le réglage"));

        var onlyEstimate = GameRatings.Combine(estimate, null)!;
        Assert.Equal((RatingSource.Estimated, GraphicsPreset.High, 77, "Bonne marge"), (onlyEstimate.Source, onlyEstimate.Preset, onlyEstimate.Score, onlyEstimate.Headline));
        Assert.Null(GameRatings.Combine(null, null));
    }

    [Fact]
    public void Prefers_captures_made_at_the_current_setting()
    {
        var captures = new[]
        {
            Capture(60, 45, preset: GraphicsPreset.Ultra),
            Capture(144, 120, preset: GraphicsPreset.Low),
            Capture(98, 71), // réglage non enregistré : supposé être le réglage actuel
        };

        var ultra = GameRatings.MeasureFrom(captures, 165, GraphicsPreset.Ultra)!;
        Assert.Equal((2, GraphicsPreset.Ultra), (ultra.Captures, ultra.Preset));

        var medium = GameRatings.MeasureFrom([captures[0], captures[1]], 165, GraphicsPreset.Medium)!; // aucune mesure en Moyen
        Assert.Equal((2, GraphicsPreset.Medium), (medium.Captures, medium.CurrentPreset));
        var rating = GameRatings.Combine(null, medium)!;
        Assert.Contains(rating.Details, d => d.StartsWith("Pas encore de mesure en Moyen"));

        var assumed = GameRatings.MeasureFrom([captures[2]], 165, GraphicsPreset.High)!;
        Assert.Equal((GraphicsPreset.High, true), (assumed.Preset, assumed.PresetAssumed));
    }
}
