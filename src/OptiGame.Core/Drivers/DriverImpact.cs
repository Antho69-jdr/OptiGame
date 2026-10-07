using OptiGame.Core.Abstractions;
using OptiGame.Core.Measurement;
using OptiGame.Core.Text;

namespace OptiGame.Core.Drivers;

/// <summary>Effet d'un changement de pilote sur un jeu, d'après les mesures de l'utilisateur (médianes des FPS moyens).</summary>
public sealed record GameDriverImpact(string Game, double FpsBefore, double FpsAfter, int CapturesBefore, int CapturesAfter)
{
    public double ChangePercent => FpsBefore <= 0 ? 0 : 100 * (FpsAfter - FpsBefore) / FpsBefore;
}

/// <summary>Bilan du dernier changement de pilote, jeu par jeu.</summary>
public sealed record DriverImpactReport(string PreviousDriver, string CurrentDriver, IReadOnlyList<GameDriverImpact> Games)
{
    /// <summary>Un jeu a perdu au moins <see cref="DriverImpacts.RegressionPercent"/> % de FPS.</summary>
    public bool HasRegression => Games.Any(g => g.ChangePercent <= -DriverImpacts.RegressionPercent);
}

/// <summary>
/// « Avant / après » d'un changement de pilote, avec les mesures de l'utilisateur : chaque capture note la version du pilote
/// graphique (<see cref="CaptureRecord.GpuDriver"/>, depuis le 2026-10-07 ; les plus anciennes n'en ont pas et sont ignorées).
/// Comparé, jeu par jeu : captures sous le pilote actuel contre captures sous le pilote précédent, au MÊME réglage graphique
/// (sinon l'écart viendrait du réglage, pas du pilote). Médiane des FPS moyens des captures fiables.
/// </summary>
public static class DriverImpacts
{
    /// <summary>Baisse à partir de laquelle on parle de régression (en deçà : variation normale d'une partie à l'autre).</summary>
    public const double RegressionPercent = 5;

    /// <summary>Version courte du pilote d'une carte : « 617.42 » pour NVIDIA, sinon la version Windows.</summary>
    public static string? DriverLabel(GpuAdapter gpu) =>
        DriverRules.VendorOf(gpu.PnpDeviceId) == GpuVendor.Nvidia ? NvidiaDrivers.FromWindowsVersion(gpu.DriverVersion) ?? gpu.DriverVersion : gpu.DriverVersion;

    /// <summary>Pilote de la carte de jeu : carte physique NVIDIA ou AMD de préférence (la carte intégrée Intel en dernier).</summary>
    public static string? CurrentDriver(IEnumerable<GpuAdapter> adapters) => adapters
        .Where(a => a.IsPhysical)
        .OrderBy(a => DriverRules.VendorOf(a.PnpDeviceId) is GpuVendor.Nvidia or GpuVendor.Amd ? 0 : 1)
        .Select(DriverLabel)
        .FirstOrDefault(v => v is not null);

    /// <param name="gameOf">Nom du jeu d'un exe (« Overwatch.exe » → « Overwatch ») ; null = pas un jeu de Mes jeux, ignoré.</param>
    public static DriverImpactReport? Compare(IReadOnlyList<CaptureRecord> captures, string currentDriver, Func<string, string?> gameOf)
    {
        var withDriver = captures.Where(c => c.GpuDriver is not null && c.Stats.IsReliable).OrderByDescending(c => c.CapturedAt).ToList();
        // Pilote précédent : celui des captures les plus récentes faites sous un autre pilote que l'actuel.
        var previous = withDriver.FirstOrDefault(c => c.GpuDriver != currentDriver)?.GpuDriver;
        if (previous is null) return null;

        var games = new List<GameDriverImpact>();
        foreach (var byGame in withDriver.GroupBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            if (gameOf(byGame.Key) is not { } name) continue;
            var after = byGame.Where(c => c.GpuDriver == currentDriver).ToList();
            var before = byGame.Where(c => c.GpuDriver == previous).ToList();
            // Même réglage graphique : celui de la capture la plus récente sous le pilote actuel.
            if (after.Count == 0) continue;
            var preset = after[0].Preset;
            after = after.Where(c => c.Preset == preset).ToList();
            before = before.Where(c => c.Preset == preset).ToList();
            if (before.Count == 0) continue;
            games.Add(new GameDriverImpact(name, Median(before), Median(after), before.Count, after.Count));
        }
        return games.Count == 0 ? null : new DriverImpactReport(previous, currentDriver, [.. games.OrderBy(g => g.ChangePercent)]);
    }

    /// <summary>« Overwatch : 165 → 158 FPS (−4 %) ».</summary>
    public static string Describe(GameDriverImpact game) =>
        $"{game.Game} : {game.FpsBefore:0} → {game.FpsAfter:0}{FrenchText.NoBreakSpace}FPS " +
        $"({(game.ChangePercent >= 0 ? "+" : "−")}{Math.Abs(game.ChangePercent).ToString("0", FrenchText.French)}{FrenchText.NoBreakSpace}%)";

    private static double Median(IReadOnlyList<CaptureRecord> captures)
    {
        var sorted = captures.Select(c => c.Stats.AverageFps).Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
