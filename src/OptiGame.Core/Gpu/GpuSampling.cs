using System.Globalization;

namespace OptiGame.Core.Gpu;

/// <summary>Un relevé d'une carte NVIDIA pendant une partie (null = valeur non fournie par la carte, « [N/A] »).</summary>
public sealed record GpuSample(int Index, int? Utilization, int? TemperatureC, double? PowerW, double? PowerLimitW, int? ClockMhz,
    bool PowerCap, bool Thermal, bool HardwareSlowdown);

/// <summary>Ce qui bride la carte graphique pendant une mesure.</summary>
public enum GpuThrottle
{
    None,

    /// <summary>Limite de puissance atteinte : comportement NORMAL de GPU Boost quand la carte tourne à fond.</summary>
    PowerLimit,

    /// <summary>Température trop haute : refroidissement insuffisant (poussière, ventilation, pâte thermique).</summary>
    Thermal,

    /// <summary>Ralentissement matériel hors température : souvent l'alimentation (bloc, câbles PCIe).</summary>
    Hardware,
}

/// <summary>Résumé des relevés d'une capture (carte la plus utilisée par le jeu).</summary>
public sealed record GpuHealth(int Samples, int? MaxTemperatureC, int? AverageClockMhz, double? AveragePowerW, double? PowerLimitW,
    double PowerCapShare, double ThermalShare, double HardwareShare)
{
    /// <summary>Part minimale des relevés pour retenir un bridage (6 secondes sur une mesure d'une minute).</summary>
    public const double Significant = 0.10;

    public GpuThrottle Throttle =>
        ThermalShare >= Significant ? GpuThrottle.Thermal
        : HardwareShare >= Significant ? GpuThrottle.Hardware
        : PowerCapShare >= Significant ? GpuThrottle.PowerLimit
        : GpuThrottle.None;
}

/// <summary>
/// Relevés de nvidia-smi pendant la mesure automatique : une ligne CSV par carte et par seconde. Champs vérifiés avec
/// « nvidia-smi --help-query-gpu » (pilote 617.14, 2026-10-03 ; les anciens noms clocks_throttle_reasons.* sont acceptés aussi).
/// hw_slowdown couvre aussi le bridage thermique matériel : il ne compte comme « matériel » que sans bridage thermique.
/// </summary>
public static class GpuSampling
{
    public const string Fields = "index,utilization.gpu,temperature.gpu,power.draw,enforced.power.limit,clocks.gr," +
                                 "clocks_event_reasons.sw_power_cap,clocks_event_reasons.sw_thermal_slowdown," +
                                 "clocks_event_reasons.hw_thermal_slowdown,clocks_event_reasons.hw_slowdown," +
                                 "clocks_event_reasons.hw_power_brake_slowdown";

    public static IReadOnlyList<string> Arguments(int intervalMs) =>
        [$"--query-gpu={Fields}", "--format=csv,noheader,nounits", "-lms", intervalMs.ToString(CultureInfo.InvariantCulture)];

    /// <summary>Null si la ligne n'a pas les 11 colonnes attendues (message d'erreur de l'outil…).</summary>
    public static GpuSample? ParseLine(string line)
    {
        var f = line.Split(',').Select(s => s.Trim()).ToArray();
        if (f.Length != 11 || Int(f[0]) is not { } index) return null;
        bool On(string s) => s.Equals("Active", StringComparison.OrdinalIgnoreCase);
        var thermal = On(f[7]) || On(f[8]);
        return new GpuSample(index, Int(f[1]), Int(f[2]), Number(f[3]), Number(f[4]), Int(f[5]),
            PowerCap: On(f[6]), Thermal: thermal, HardwareSlowdown: !thermal && (On(f[9]) || On(f[10])));
    }

    /// <summary>Résumé pour la carte la plus utilisée ; null sous 5 relevés (mesure trop courte ou outil absent).</summary>
    public static GpuHealth? Summarize(IReadOnlyList<GpuSample> samples)
    {
        var busiest = samples.GroupBy(s => s.Index)
            .OrderByDescending(g => g.Average(s => s.Utilization ?? 0))
            .FirstOrDefault()?.ToList();
        if (busiest is null || busiest.Count < 5) return null;

        double Share(Func<GpuSample, bool> flag) => busiest.Count(flag) / (double)busiest.Count;
        var temperatures = busiest.Where(s => s.TemperatureC is not null).Select(s => s.TemperatureC!.Value).ToList();
        var clocks = busiest.Where(s => s.ClockMhz is not null).Select(s => s.ClockMhz!.Value).ToList();
        var power = busiest.Where(s => s.PowerW is not null).Select(s => s.PowerW!.Value).ToList();
        return new GpuHealth(busiest.Count,
            temperatures.Count > 0 ? temperatures.Max() : null,
            clocks.Count > 0 ? (int)Math.Round(clocks.Average()) : null,
            power.Count > 0 ? Math.Round(power.Average(), 1) : null,
            busiest.Select(s => s.PowerLimitW).FirstOrDefault(p => p is not null),
            Share(s => s.PowerCap), Share(s => s.Thermal), Share(s => s.HardwareSlowdown));
    }

    private static int? Int(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
