using System.Globalization;
using OptiGame.Core.Rating;
using OptiGame.Core.Text;

namespace OptiGame.Core.Measurement;

/// <summary>
/// Résumé d'une mesure (ou de deux comparées) à copier et partager où l'on veut : jeu, FPS moyens, 1 % les plus lents, temps
/// d'image P99, réglage du jeu s'il est connu, carte graphique et pilote. Rien n'est envoyé par OptiGame : c'est l'utilisateur qui
/// colle le texte. Pas de chemin ni de nom de PC.
/// </summary>
public static class CaptureSummary
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <param name="game">Nom du jeu (profil), sinon l'exe mesuré.</param>
    /// <param name="gpu">Carte graphique du PC, ex. « NVIDIA GeForce RTX 3070 » ; null = non citée.</param>
    public static string Of(CaptureRecord capture, string game, string? gpu, DateTime localDate)
    {
        var s = capture.Stats;
        var parts = new List<string>
        {
            $"{Fps(s.AverageFps)} FPS moyens",
            $"1{FrenchText.NarrowNoBreakSpace}% les plus lents : {Fps(s.OnePercentLowFps)} FPS",
            $"temps d'image P99 : {Ms(s.P99FrameTimeMs)} ms",
        };
        if (capture.Preset is { } preset) parts.Add($"réglage {GameRatings.Label(preset)}");
        return $"{game} — {string.Join(" · ", parts)}{Machine(capture, gpu)} · mesuré {On(localDate)} avec OptiGame" +
               (capture.Stats.IsReliable ? "" : " (mesure courte : peu fiable)");
    }

    /// <summary>
    /// Deux mesures comparées : « Portal 2 — avant / après : FPS moyens 250,1 → 288,4 (+15 %) · … » ; deux jeux différents sont
    /// nommés tous les deux (« ARC Raiders → Overwatch »), sinon le résumé ferait croire à un avant / après du même jeu.
    /// </summary>
    public static string Compare(CaptureRecord before, CaptureRecord after, string gameBefore, string gameAfter, string? gpu)
    {
        var c = new FrameStatsComparison(before.Stats, after.Stats);
        var parts = new List<string>
        {
            $"FPS moyens {Fps(before.Stats.AverageFps)} → {Fps(after.Stats.AverageFps)} ({Percent(c.AverageFpsChangePercent)})",
            $"1{FrenchText.NarrowNoBreakSpace}% les plus lents {Fps(before.Stats.OnePercentLowFps)} → {Fps(after.Stats.OnePercentLowFps)} ({Percent(c.OnePercentLowChangePercent)})",
            $"P99 {Ms(before.Stats.P99FrameTimeMs)} → {Ms(after.Stats.P99FrameTimeMs)} ms ({Percent(c.P99FrameTimeChangePercent)})",
        };
        var games = gameBefore.Equals(gameAfter, StringComparison.OrdinalIgnoreCase) ? $"{gameBefore} — avant / après" : $"{gameBefore} → {gameAfter}";
        return $"{games} : {string.Join(" · ", parts)}{Machine(after, gpu)} · mesuré avec OptiGame";
    }

    private static string Machine(CaptureRecord capture, string? gpu) =>
        gpu is null ? "" : $" · {gpu}{(capture.GpuDriver is { } driver ? $" (pilote {driver})" : "")}";

    private static string On(DateTime date) => $"le {FrenchText.Date(date)}";

    private static string Fps(double value) => value.ToString("0.0", French);

    private static string Ms(double value) => value.ToString("0.0", French);

    private static string Percent(double value) =>
        $"{(value >= 0 ? "+" : "−")}{Math.Abs(value).ToString("0", French)}{FrenchText.NarrowNoBreakSpace}%";
}
