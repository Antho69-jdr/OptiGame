using OptiGame.Core.Profiles;
using OptiGame.Core.Text;

namespace OptiGame.Core.Measurement;

/// <summary>Temps processeur cumulé d'un processus à un instant (relevé avant et après la mesure).</summary>
public sealed record ProcessCpuSample(int ProcessId, string ExeName, string? Path, TimeSpan CpuTime);

/// <summary>Programme qui a utilisé le processeur pendant une mesure : part du processeur ENTIER (tous les cœurs), en %.</summary>
public sealed record BackgroundProgram(string ExeName, string? Path, double CpuPercent, int Instances);

/// <summary>
/// Programmes gourmands pendant une partie : deux relevés du temps processeur de chaque processus de la session (début et fin
/// des 60 s de la mesure automatique), rien entre les deux. Les processus d'un même exe sont additionnés (Chrome en a des
/// dizaines). Écartés : le jeu, OptiGame et ses outils de mesure, les composants de Windows et les processus protégés (que
/// « Programmes à fermer » refuse de toute façon).
/// </summary>
public static class BackgroundLoad
{
    /// <summary>Seuil d'affichage : 2 % du processeur entier (1/4 de cœur sur un processeur 12 threads).</summary>
    public const double MinimumPercent = 2;

    public const int MaxPrograms = 5;

    /// <summary>Outils lancés par OptiGame pendant la mesure.</summary>
    private static readonly HashSet<string> OwnTools = new(StringComparer.OrdinalIgnoreCase) { "nvidia-smi.exe" };

    public static IReadOnlyList<BackgroundProgram> Summarize(IReadOnlyList<ProcessCpuSample> before, IReadOnlyList<ProcessCpuSample> after,
        TimeSpan elapsed, int processorCount, string gameExePath, Func<string?, bool> isWindowsComponent)
    {
        if (elapsed <= TimeSpan.Zero || processorCount <= 0) return [];
        var start = before.ToDictionary(s => (s.ProcessId, s.ExeName), s => s.CpuTime);
        var total = elapsed.TotalMilliseconds * processorCount;
        var gameExe = System.IO.Path.GetFileName(gameExePath);

        return after
            .Where(s => !s.ExeName.Equals(gameExe, StringComparison.OrdinalIgnoreCase)
                        && !ProfileValidator.IsProtected(s.ExeName)
                        && !OwnTools.Contains(s.ExeName)
                        && !s.ExeName.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase)
                        && !isWindowsComponent(s.Path))
            // Processus lancé pendant la mesure : compté depuis zéro.
            .Select(s => (Sample: s, Used: s.CpuTime - start.GetValueOrDefault((s.ProcessId, s.ExeName), TimeSpan.Zero)))
            .Where(x => x.Used > TimeSpan.Zero)
            .GroupBy(x => x.Sample.ExeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BackgroundProgram(g.Key, g.Select(x => x.Sample.Path).FirstOrDefault(p => p is not null),
                Math.Round(100 * g.Sum(x => x.Used.TotalMilliseconds) / total, 1), g.Count()))
            .Where(p => p.CpuPercent >= MinimumPercent)
            .OrderByDescending(p => p.CpuPercent)
            .Take(MaxPrograms)
            .ToList();
    }

    /// <summary>« chrome.exe 12 % du processeur (23 processus), Discord.exe 4 % ».</summary>
    public static string Describe(IEnumerable<BackgroundProgram> programs) =>
        string.Join(", ", programs.Select(p =>
            $"{p.ExeName} {p.CpuPercent.ToString("0.#", FrenchText.French)}{FrenchText.NoBreakSpace}%" + (p.Instances > 1 ? $" ({p.Instances} processus)" : "")));
}
