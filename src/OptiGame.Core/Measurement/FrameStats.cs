namespace OptiGame.Core.Measurement;

/// <summary>
/// Statistiques d'une capture, calculées sur MsBetweenPresents (cadence à laquelle le jeu produit ses images).
/// Définitions (affichées dans l'UI, car les outils ne calculent pas tous ces valeurs de la même façon) :
/// - FPS moyens = nombre d'images / durée totale (moyenne pondérée par le temps) ;
/// - 1 % low = FPS moyens sur les 1 % d'images les plus longues (idem 0,1 %) ;
/// - P99 = frametime du 99e centile (99 % des images sont plus rapides).
/// </summary>
public sealed record FrameStats(
    int FrameCount,
    double DurationSeconds,
    double AverageFps,
    double OnePercentLowFps,
    double PointOnePercentLowFps,
    double MedianFrameTimeMs,
    double P99FrameTimeMs,
    double MaxFrameTimeMs)
{
    /// <summary>Sous ce nombre d'images, les « 1 % low » ne sont pas significatifs.</summary>
    public const int MinimumFrames = 100;

    public static FrameStats Compute(IReadOnlyList<double> frameTimesMs)
    {
        var valid = frameTimesMs.Where(t => t > 0 && double.IsFinite(t)).ToArray();
        if (valid.Length == 0)
        {
            throw new InvalidOperationException("Aucune image exploitable dans la capture.");
        }

        var total = valid.Sum();
        var sorted = valid.OrderBy(t => t).ToArray();
        return new FrameStats(
            valid.Length,
            total / 1000.0,
            valid.Length * 1000.0 / total,
            LowFps(sorted, 0.01),
            LowFps(sorted, 0.001),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.99),
            sorted[^1]);
    }

    public bool IsReliable => FrameCount >= MinimumFrames;

    /// <summary>FPS moyens (pondérés par le temps) sur la fraction d'images la plus lente.</summary>
    private static double LowFps(double[] sortedAscending, double fraction)
    {
        var count = Math.Max(1, (int)Math.Ceiling(sortedAscending.Length * fraction));
        var worst = sortedAscending[^count..];
        return count * 1000.0 / worst.Sum();
    }

    /// <summary>Centile par interpolation linéaire entre rangs.</summary>
    private static double Percentile(double[] sortedAscending, double p)
    {
        if (sortedAscending.Length == 1) return sortedAscending[0];
        var rank = p * (sortedAscending.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = Math.Min(low + 1, sortedAscending.Length - 1);
        return sortedAscending[low] + (sortedAscending[high] - sortedAscending[low]) * (rank - low);
    }
}

/// <summary>Écart entre une capture de référence (« avant ») et une autre (« après »).</summary>
public sealed record FrameStatsComparison(FrameStats Before, FrameStats After)
{
    public double AverageFpsChangePercent => Percent(Before.AverageFps, After.AverageFps);

    public double OnePercentLowChangePercent => Percent(Before.OnePercentLowFps, After.OnePercentLowFps);

    public double PointOnePercentLowChangePercent => Percent(Before.PointOnePercentLowFps, After.PointOnePercentLowFps);

    /// <summary>Négatif = frametimes plus courts = mieux.</summary>
    public double P99FrameTimeChangePercent => Percent(Before.P99FrameTimeMs, After.P99FrameTimeMs);

    private static double Percent(double before, double after) => before == 0 ? 0 : (after - before) / before * 100.0;
}
