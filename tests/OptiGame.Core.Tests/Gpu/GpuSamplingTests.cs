using OptiGame.Core.Gpu;
using OptiGame.Core.Measurement;
using OptiGame.Core.Rating;

namespace OptiGame.Core.Tests.Gpu;

public sealed class GpuSamplingTests
{
    private static IReadOnlyList<string> RealIdleLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Gpu", "Samples", "nvidia-smi-samples-idle.csv"));

    /// <summary>Ligne construite au format réel : index, utilisation, °C, W, limite, MHz, puis les 5 indicateurs de bridage.</summary>
    private static string Line(int index = 0, int use = 98, int temp = 70, bool powerCap = false, bool swThermal = false, bool hwThermal = false,
        bool hwSlowdown = false, bool powerBrake = false)
    {
        static string On(bool active) => active ? "Active" : "Not Active";
        return $"{index}, {use}, {temp}, 215.40, 220.00, 1905, {On(powerCap)}, {On(swThermal)}, {On(hwThermal)}, {On(hwSlowdown)}, {On(powerBrake)}";
    }

    [Fact]
    public void Reads_real_nvidia_smi_lines()
    {
        var samples = RealIdleLines().Select(GpuSampling.ParseLine).ToList(); // RTX 3070 au repos, pilote 617.14
        Assert.All(samples, Assert.NotNull);
        Assert.Equal(new GpuSample(0, 23, 40, 23.49, 220.00, 555, false, false, false), samples[0]);
        Assert.Null(GpuSampling.ParseLine("NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver."));
        Assert.Null(GpuSampling.ParseLine("0, [N/A], 40")); // colonnes manquantes
        Assert.Equal((null, null), (GpuSampling.ParseLine("0, 50, 40, [N/A], [N/A], 1500, Not Active, Not Active, Not Active, Not Active, Not Active")!.PowerW,
            GpuSampling.ParseLine("0, 50, 40, [N/A], [N/A], 1500, Not Active, Not Active, Not Active, Not Active, Not Active")!.PowerLimitW));
    }

    private static GpuHealth Summarize(params string[] lines) => GpuSampling.Summarize(lines.Select(l => GpuSampling.ParseLine(l)!).ToList())!;

    [Fact]
    public void Finds_what_slows_the_card_down()
    {
        var normal = Enumerable.Repeat(Line(), 54).ToArray();
        Assert.Equal(GpuThrottle.None, Summarize(normal).Throttle);

        var hot = normal.Concat(Enumerable.Repeat(Line(temp: 87, swThermal: true), 6)).ToArray(); // 10 % du temps
        var thermal = Summarize(hot);
        Assert.Equal((GpuThrottle.Thermal, 87), (thermal.Throttle, thermal.MaxTemperatureC));

        // hw_slowdown accompagne le bridage thermique matériel : compté comme thermique, pas comme alimentation.
        Assert.Equal(GpuThrottle.Thermal, Summarize(normal.Concat(Enumerable.Repeat(Line(hwThermal: true, hwSlowdown: true), 6)).ToArray()).Throttle);
        Assert.Equal(GpuThrottle.Hardware, Summarize(normal.Concat(Enumerable.Repeat(Line(hwSlowdown: true, powerBrake: true), 6)).ToArray()).Throttle);

        var capped = Summarize(Enumerable.Repeat(Line(powerCap: true), 60).ToArray());
        Assert.Equal((GpuThrottle.PowerLimit, 1.0, 215.4, 220.0), (capped.Throttle, capped.PowerCapShare, capped.AveragePowerW, capped.PowerLimitW));
    }

    [Fact]
    public void Keeps_the_card_the_game_uses_and_needs_enough_samples()
    {
        var twoCards = Enumerable.Range(0, 10).SelectMany(_ => new[] { Line(index: 0, use: 3, temp: 45), Line(index: 1, use: 97, temp: 75) }).ToArray();
        Assert.Equal(75, Summarize(twoCards).MaxTemperatureC);
        Assert.Null(GpuSampling.Summarize(RealIdleLines().Select(l => GpuSampling.ParseLine(l)!).ToList())); // 3 relevés < 5
    }

    [Fact]
    public void The_rating_explains_thermal_throttling_and_reassures_about_the_power_limit()
    {
        var stats = new FrameStats(5000, 60, 70, 50, 40, 14, 20, 40);
        var hot = Summarize(Enumerable.Repeat(Line(), 48).Concat(Enumerable.Repeat(Line(temp: 88, swThermal: true), 12)).ToArray());
        var rating = GameRatings.Combine(null, GameRatings.MeasureFrom([new(stats, new FrameLoad(0.98, 0.01), GraphicsPreset.High, hot)], 165))!;
        Assert.Contains("dépoussiérez", rating.Advice);
        Assert.Contains(rating.Details, d => d.Contains("88 °C au plus") && d.Contains("bridée par la température 20 % du temps"));

        var atLimit = Summarize(Enumerable.Repeat(Line(powerCap: true), 60).ToArray());
        var normal = GameRatings.Combine(null, GameRatings.MeasureFrom([new(stats, new FrameLoad(0.98, 0.01), GraphicsPreset.High, atLimit)], 165))!;
        Assert.DoesNotContain("dépoussiérez", normal.Advice);
        Assert.Contains(normal.Details, d => d.Contains("215 W sur 220 W autorisés") && d.Contains("normal quand elle tourne à fond"));
    }
}
