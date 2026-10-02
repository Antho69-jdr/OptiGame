using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptiGame.Core.Measurement;

namespace OptiGame.Core.Rating;

public enum GraphicsPreset
{
    Low,
    Medium,
    High,
    Ultra,
}

public enum RatingSource
{
    /// <summary>D'après la configuration requise du jeu (avant d'avoir mesuré).</summary>
    Estimated,

    /// <summary>D'après les FPS réellement mesurés (PresentMon).</summary>
    Measured,
}

/// <summary>Un niveau de configuration requise (minimum ou recommandé) : cartes graphiques citées et mémoire vive.</summary>
public sealed record RequirementLevel(IReadOnlyList<GpuPerformance.Match> Gpus, int? MemoryGb, string GraphicsText);

public sealed record SystemRequirements(RequirementLevel? Minimum, RequirementLevel? Recommended);

/// <summary>Le PC : carte graphique (indice), mémoire, résolution et fréquence de l'écran de jeu.</summary>
public sealed record PcSpecs(string GpuName, GpuPerformance.Match? Gpu, int MemoryGb, int Width, int Height, int RefreshHz);

/// <summary>Note d'un jeu, sur 100, avec le réglage graphique conseillé et l'explication.</summary>
public sealed record GameRating(int Score, RatingSource Source, GraphicsPreset? Preset, bool BelowMinimum, string Headline, string Advice,
    IReadOnlyList<string> Details);

/// <summary>
/// Configurations requises publiées par Steam (store.steampowered.com/api/appdetails?appids=…&amp;l=english). Format vérifié le
/// 2026-10-02 sur PUBG, Overwatch, Scrap Mechanic et Void Crew (échantillons dans les tests) : pc_requirements.minimum /
/// recommended en HTML, une ligne <c>&lt;li&gt;&lt;strong&gt;Graphics:&lt;/strong&gt; …&lt;/li&gt;</c> et une ligne « Memory: 8 GB RAM ».
/// </summary>
public static partial class SteamRequirements
{
    public static Uri AppDetailsUri(string appId) => new($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic&l=english");

    public static SystemRequirements? ParseAppDetails(string json, string appId)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(appId, out var app) || !app.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !app.TryGetProperty("data", out var data) || !data.TryGetProperty("pc_requirements", out var pc) || pc.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var minimum = ParseLevel(pc.TryGetProperty("minimum", out var min) && min.ValueKind == JsonValueKind.String ? min.GetString() : null);
        var recommended = ParseLevel(pc.TryGetProperty("recommended", out var rec) && rec.ValueKind == JsonValueKind.String ? rec.GetString() : null);
        return minimum is null && recommended is null ? null : new SystemRequirements(minimum, recommended);
    }

    public static RequirementLevel? ParseLevel(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        string? graphics = null;
        int? memory = null;
        foreach (Match item in ListItem().Matches(html))
        {
            var text = WebUtility.HtmlDecode(Tags().Replace(item.Groups[1].Value, " "));
            text = Spaces().Replace(text, " ").Trim();
            if (text.StartsWith("Graphics:", StringComparison.OrdinalIgnoreCase)) graphics = text["Graphics:".Length..].Trim();
            else if (text.StartsWith("Memory:", StringComparison.OrdinalIgnoreCase) && Memory().Match(text) is { Success: true } m)
            {
                var amount = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                memory = m.Groups[2].Value.Equals("MB", StringComparison.OrdinalIgnoreCase) ? (int)Math.Ceiling(amount / 1024) : (int)Math.Ceiling(amount);
            }
        }
        return graphics is null && memory is null ? null : new RequirementLevel(graphics is null ? [] : GpuPerformance.Find(graphics), memory, graphics ?? "");
    }

    [GeneratedRegex(@"<li>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(GB|MB)", RegexOptions.IgnoreCase)]
    private static partial Regex Memory();
}

/// <summary>
/// Note d'un jeu par rapport au PC.
/// <list type="bullet">
/// <item>ESTIMATION (avant d'avoir mesuré) : carte graphique du PC comparée aux cartes minimum et recommandée (indices de
/// <see cref="GpuPerformance"/>, même fabricant de préférence, la plus faible citée). Les configurations requises visent le
/// 1080p : l'indice du PC est ramené en équivalent 1080p (pixels^0,7). Recommandée atteinte = « Élevé ».</item>
/// <item>MESURE (PresentMon) : fluidité = 60 % FPS moyens + 40 % 1 % low, rapportée à min(fréquence de l'écran, 120 Hz) ;
/// médiane des 5 dernières captures fiables. Le réglage estimé est relevé d'un cran s'il reste de la marge (FPS au-delà de la
/// fréquence de l'écran) et baissé d'un cran si la partie est peu fluide.</item>
/// </list>
/// </summary>
public static class GameRatings
{
    public static string Label(GraphicsPreset preset) => preset switch
    {
        GraphicsPreset.Low => "Bas",
        GraphicsPreset.Medium => "Moyen",
        GraphicsPreset.High => "Élevé",
        _ => "Ultra",
    };

    private const double FullHdPixels = 1920 * 1080;

    public sealed record Estimate(int Score, GraphicsPreset Preset, bool BelowMinimum, IReadOnlyList<string> Details);

    public sealed record Measurement(int Score, int Captures, double AverageFps, double OnePercentLowFps, bool HasHeadroom, int RefreshHz);

    public static Estimate? EstimateFrom(PcSpecs pc, SystemRequirements requirements)
    {
        if (pc.Gpu is not { } gpu) return null;
        var minGpu = Pick(requirements.Minimum, gpu.Vendor);
        var recGpu = Pick(requirements.Recommended, gpu.Vendor);
        if (minGpu is null && recGpu is null) return null;

        double rec = recGpu?.Index ?? minGpu!.Index * 1.8;
        double min = minGpu?.Index ?? rec * 0.5;
        var pixels = Math.Max(0.5, pc.Width * (double)pc.Height / FullHdPixels);
        var effective = gpu.Index / Math.Pow(pixels, 0.7);
        var ratio = effective / rec;

        var below = effective < min;
        var preset = below || ratio < 0.6 ? GraphicsPreset.Low : ratio < 1.0 ? GraphicsPreset.Medium : ratio < 1.6 ? GraphicsPreset.High : GraphicsPreset.Ultra;
        double score = below ? 15 + 20 * Clamp(effective / min)
            : ratio < 0.6 ? 35 + 15 * Clamp((effective - min) / Math.Max(1, 0.6 * rec - min))
            : ratio < 1.0 ? 50 + 20 * (ratio - 0.6) / 0.4
            : ratio < 1.6 ? 70 + 15 * (ratio - 1.0) / 0.6
            : 85 + 15 * Clamp((ratio - 1.6) / 1.0);

        var details = new List<string>
        {
            $"Votre carte : {gpu.Model} (indice {gpu.Index}) en {pc.Width}×{pc.Height}, soit ≈ {effective:0} en équivalent 1080p.",
        };
        if (recGpu is not null) details.Add($"Recommandé par le jeu : {recGpu.Model} (indice {recGpu.Index}).");
        if (minGpu is not null) details.Add($"Minimum : {minGpu.Model} (indice {minGpu.Index}).");

        // Mémoire vive : sous le minimum = injouable ; sous le recommandé = pas au-delà de « Moyen ».
        if (requirements.Minimum?.MemoryGb is { } minMemory && pc.MemoryGb < minMemory)
        {
            below = true;
            preset = GraphicsPreset.Low;
            score = Math.Min(score, 30);
        }
        else if (requirements.Recommended?.MemoryGb is { } recMemory && pc.MemoryGb < recMemory && preset > GraphicsPreset.Medium)
        {
            preset = GraphicsPreset.Medium;
            score = Math.Min(score, 69);
        }
        var memoryNeeds = requirements.Recommended?.MemoryGb ?? requirements.Minimum?.MemoryGb;
        details.Add($"Mémoire vive : {pc.MemoryGb} Go" + (memoryNeeds is { } needed ? $" (le jeu demande {needed} Go)." : "."));

        return new Estimate((int)Math.Round(score), preset, below, details);
    }

    /// <summary>Note mesurée : médiane des 5 dernières captures fiables ; null s'il n'y en a aucune.</summary>
    public static Measurement? MeasureFrom(IReadOnlyList<FrameStats> capturesNewestFirst, int refreshHz)
    {
        var reliable = capturesNewestFirst.Where(c => c.IsReliable).Take(5).ToList();
        if (reliable.Count == 0) return null;

        var target = Math.Clamp(refreshHz, 30, 120);
        int ScoreOf(FrameStats s) => (int)Math.Round(100 * Math.Pow(Math.Min(1, (0.6 * s.AverageFps + 0.4 * s.OnePercentLowFps) / target), 0.7));
        var ordered = reliable.OrderBy(ScoreOf).ToList();
        var median = ordered[ordered.Count / 2];
        var headroom = median.AverageFps >= 1.15 * refreshHz && median.OnePercentLowFps >= 0.75 * refreshHz;
        return new Measurement(ScoreOf(median), reliable.Count, median.AverageFps, median.OnePercentLowFps, headroom, refreshHz);
    }

    /// <summary>Note affichée : la mesure si elle existe (le réglage estimé est alors ajusté), sinon l'estimation.</summary>
    public static GameRating? Combine(Estimate? estimate, Measurement? measured)
    {
        if (measured is not null)
        {
            var preset = estimate?.Preset;
            var advice = "Réglages adaptés à votre PC.";
            if (measured.HasHeadroom)
            {
                if (preset is { } p && p < GraphicsPreset.Ultra) preset = p + 1;
                advice = "Il reste de la marge : vous pouvez monter les réglages d'un cran.";
            }
            else if (measured.Score < 50)
            {
                if (preset is { } p && p > GraphicsPreset.Low) preset = p - 1;
                advice = "Baissez les réglages d'un cran pour gagner en fluidité.";
            }
            var headline = measured.Score >= 85 ? "Très fluide" : measured.Score >= 70 ? "Fluide" : measured.Score >= 50 ? "Correct" : "Peu fluide";
            var details = new List<string>
            {
                $"Mesuré sur {measured.Captures} capture(s) : {measured.AverageFps:0} FPS moyens, 1 % low {measured.OnePercentLowFps:0} FPS (écran {measured.RefreshHz} Hz).",
            };
            if (estimate is not null) details.AddRange(estimate.Details);
            return new GameRating(measured.Score, RatingSource.Measured, preset, false, headline, advice, details);
        }

        if (estimate is null) return null;
        return new GameRating(estimate.Score, RatingSource.Estimated, estimate.Preset, estimate.BelowMinimum,
            estimate.BelowMinimum ? "Sous la configuration minimale" : $"Réglages {Label(estimate.Preset).ToLowerInvariant()} conseillés",
            "Estimation d'après la configuration requise du jeu ; une partie mesurée la remplacera.", estimate.Details);
    }

    /// <summary>Carte d'un niveau de configuration : du même fabricant de préférence, la plus faible citée (une seule suffit).</summary>
    private static GpuPerformance.Match? Pick(RequirementLevel? level, string vendor)
    {
        if (level is null || level.Gpus.Count == 0) return null;
        var sameVendor = level.Gpus.Where(g => g.Vendor == vendor).ToList();
        return (sameVendor.Count > 0 ? sameVendor : level.Gpus).MinBy(g => g.Index);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
