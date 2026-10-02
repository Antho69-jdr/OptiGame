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

/// <summary>Ce qui limite les FPS pendant une capture.</summary>
public enum Bottleneck
{
    /// <summary>Carte graphique occupée en permanence : baisser les réglages fait gagner des FPS.</summary>
    Gpu,

    /// <summary>La carte graphique attend le processeur : baisser les graphismes ne change presque rien.</summary>
    Cpu,

    /// <summary>FPS plafonnés (limiteur du jeu, V-Sync) : la carte graphique a de la marge.</summary>
    FrameCap,
}

/// <summary>Un niveau de configuration requise (minimum ou recommandé) : cartes graphiques citées et mémoire vive.</summary>
public sealed record RequirementLevel(IReadOnlyList<GpuPerformance.Match> Gpus, int? MemoryGb, string GraphicsText);

/// <param name="Source">D'où vient la configuration requise (affiché avec la note).</param>
/// <param name="SourceUrl">Page à citer (PCGamingWiki : licence CC BY-NC-SA) ; null pour Steam, déjà accessible par « Page Steam ».</param>
public sealed record SystemRequirements(RequirementLevel? Minimum, RequirementLevel? Recommended, string Source = "Steam", string? SourceUrl = null);

/// <summary>Le PC : carte graphique (indice), mémoire, résolution et fréquence de l'écran de jeu.</summary>
public sealed record PcSpecs(string GpuName, GpuPerformance.Match? Gpu, int MemoryGb, int Width, int Height, int RefreshHz);

/// <summary>Note d'un jeu, sur 100, avec le réglage graphique conseillé et l'explication.</summary>
public sealed record GameRating(int Score, RatingSource Source, GraphicsPreset? Preset, bool BelowMinimum, string Headline, string Advice,
    IReadOnlyList<string> Details, string? RequirementsSource = null, string? RequirementsUrl = null);

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
/// médiane des 5 dernières captures fiables (de préférence celles faites au réglage actuel du jeu). OptiGame ne peut pas lire
/// les réglages d'un jeu : le conseil part du réglage INDIQUÉ par l'utilisateur, et s'appuie sur ce qui limite les FPS
/// (<see cref="Bottleneck"/>, d'après la charge de la carte graphique). Réglage non indiqué = conseil relatif seulement.</item>
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

    public sealed record Estimate(int Score, GraphicsPreset Preset, bool BelowMinimum, IReadOnlyList<string> Details, string Source = "Steam",
        string? SourceUrl = null);

    /// <summary>Une capture du jeu : statistiques, charge (null = inconnue) et réglage indiqué à ce moment-là.</summary>
    public sealed record MeasuredCapture(FrameStats Stats, FrameLoad? Load, GraphicsPreset? Preset);

    /// <param name="Preset">Réglage du jeu pendant les mesures (null = non indiqué).</param>
    /// <param name="PresetAssumed">Captures sans réglage enregistré : on suppose le réglage indiqué actuellement.</param>
    /// <param name="CurrentPreset">Réglage indiqué actuellement (peut différer de celui des mesures).</param>
    public sealed record Measurement(int Score, int Captures, double AverageFps, double OnePercentLowFps, bool HasHeadroom, int RefreshHz,
        GraphicsPreset? Preset = null, bool PresetAssumed = false, GraphicsPreset? CurrentPreset = null, FrameLoad? Load = null,
        Bottleneck? Bottleneck = null);

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

        return new Estimate((int)Math.Round(score), preset, below, details, requirements.Source, requirements.SourceUrl);
    }

    /// <summary>
    /// Note mesurée : médiane des 5 dernières captures fiables, de préférence celles faites au réglage indiqué actuellement
    /// (une capture sans réglage enregistré est supposée faite à ce réglage) ; null s'il n'y en a aucune.
    /// </summary>
    public static Measurement? MeasureFrom(IReadOnlyList<MeasuredCapture> capturesNewestFirst, int refreshHz, GraphicsPreset? currentPreset = null)
    {
        var reliable = capturesNewestFirst.Where(c => c.Stats.IsReliable).ToList();
        if (reliable.Count == 0) return null;
        var atCurrent = currentPreset is null ? [] : reliable.Where(c => c.Preset is null || c.Preset == currentPreset).ToList();
        var chosen = (atCurrent.Count > 0 ? atCurrent : reliable).Take(5).ToList();

        var target = Math.Clamp(refreshHz, 30, 120);
        int ScoreOf(FrameStats s) => (int)Math.Round(100 * Math.Pow(Math.Min(1, (0.6 * s.AverageFps + 0.4 * s.OnePercentLowFps) / target), 0.7));
        var median = chosen.OrderBy(c => ScoreOf(c.Stats)).ToList()[chosen.Count / 2];
        var stats = median.Stats;
        var headroom = stats.AverageFps >= 1.15 * refreshHz && stats.OnePercentLowFps >= 0.75 * refreshHz;
        var preset = median.Preset ?? (atCurrent.Count > 0 ? currentPreset : null);
        return new Measurement(ScoreOf(stats), chosen.Count, stats.AverageFps, stats.OnePercentLowFps, headroom, refreshHz,
            preset, median.Preset is null && preset is not null, currentPreset, median.Load,
            median.Load is { } load ? Classify(load, stats.AverageFps, refreshHz) : null);
    }

    /// <summary>
    /// Ce qui limite les FPS. Carte graphique occupée 85 % du temps ou plus = elle limite. Sinon : FPS plafonnés si le jeu
    /// attend volontairement (15 % du temps ou plus : limiteur, V-Sync — Overwatch en V-Sync le 2026-10-02 : attente 64 %) ou si
    /// les FPS collent à la fréquence de l'écran (filet de sécurité : selon le jeu, l'attente de la V-Sync peut se faire dans
    /// Present et ne pas apparaître dans CPUWait) ; sinon le processeur limite.
    /// </summary>
    public static Bottleneck Classify(FrameLoad load, double averageFps, int refreshHz) =>
        load.GpuBusy >= 0.85 ? Bottleneck.Gpu
        : load.CpuWait >= 0.15 || Math.Abs(averageFps - refreshHz) <= 0.03 * refreshHz ? Bottleneck.FrameCap
        : Bottleneck.Cpu;

    /// <summary>Note affichée : la mesure si elle existe (conseil à partir du réglage indiqué), sinon l'estimation.</summary>
    public static GameRating? Combine(Estimate? estimate, Measurement? measured)
    {
        if (measured is not null)
        {
            var (step, advice) = Advise(measured);
            GraphicsPreset? preset = null;
            if (measured.Preset is { } played)
            {
                var target = (int)played + step;
                if (target > (int)GraphicsPreset.Ultra)
                {
                    preset = GraphicsPreset.Ultra;
                    advice = "Déjà en Ultra, et il reste de la marge : rien à changer.";
                }
                else if (target < (int)GraphicsPreset.Low)
                {
                    preset = GraphicsPreset.Low;
                    advice = "Déjà en Bas : baissez la résolution, ou activez l'upscaling (DLSS, FSR, XeSS) si le jeu le propose.";
                }
                else
                {
                    preset = (GraphicsPreset)target;
                }
            }

            var headline = measured.Score >= 85 ? "Très fluide" : measured.Score >= 70 ? "Fluide" : measured.Score >= 50 ? "Correct" : "Peu fluide";
            var details = new List<string>
            {
                $"Mesuré sur {measured.Captures} capture(s) : {measured.AverageFps:0} FPS moyens, 1 % low {measured.OnePercentLowFps:0} FPS (écran {measured.RefreshHz} Hz).",
            };
            var gpu = Percent(measured.Load?.GpuBusy);
            details.Add(measured.Bottleneck switch
            {
                Bottleneck.Gpu => $"Limité par la carte graphique (occupée {gpu} % du temps).",
                Bottleneck.FrameCap => $"Limité par un plafond de FPS (limiteur du jeu ou V-Sync) ; carte graphique occupée {gpu} % du temps.",
                Bottleneck.Cpu => $"Limité par le processeur ; carte graphique occupée {gpu} % du temps seulement.",
                _ => "Charge de la carte graphique inconnue pour ces mesures.",
            });
            if (measured.Preset is { } during)
            {
                details.Add(measured.PresetAssumed
                    ? $"Réglage pendant les mesures supposé : {Label(during)} (celui que vous avez indiqué)."
                    : $"Réglage du jeu pendant les mesures : {Label(during)}.");
                if (measured.CurrentPreset is { } current && current != during)
                {
                    details.Add($"Pas encore de mesure en {Label(current)} : la note vient de vos parties en {Label(during)}.");
                }
            }
            else
            {
                details.Add("Indiquez le réglage utilisé dans le jeu pour obtenir un réglage conseillé (OptiGame ne peut pas le lire).");
            }
            if (estimate is not null) details.AddRange(estimate.Details);
            return new GameRating(measured.Score, RatingSource.Measured, preset, false, headline, advice, details, estimate?.Source, estimate?.SourceUrl);
        }

        if (estimate is null) return null;
        return new GameRating(estimate.Score, RatingSource.Estimated, estimate.Preset, estimate.BelowMinimum,
            estimate.BelowMinimum ? "Sous la configuration minimale"
                : estimate.Score >= 85 ? "Large marge" : estimate.Score >= 70 ? "Bonne marge" : estimate.Score >= 50 ? "Marge suffisante" : "Juste",
            "Une partie mesurée remplacera cette estimation.", estimate.Details, estimate.Source, estimate.SourceUrl);
    }

    /// <summary>Conseil d'après la mesure : cran à monter (+1), garder (0) ou baisser (−1), et son explication.</summary>
    private static (int Step, string Advice) Advise(Measurement m)
    {
        var gpu = Percent(m.Load?.GpuBusy);
        var gpuIdle = m.Load is { GpuBusy: < 0.7 };
        return m.Bottleneck switch
        {
            Bottleneck.Gpu when m.HasHeadroom => (1, "Vos FPS dépassent nettement la fréquence de l'écran : vous pouvez monter les réglages d'un cran."),
            Bottleneck.Gpu when m.Score < 50 => (-1, $"La carte graphique tourne à fond ({gpu} % du temps) : baissez les réglages d'un cran pour gagner des FPS."),
            Bottleneck.Gpu when m.Score < 70 => (0, $"La carte graphique tourne à fond ({gpu} % du temps) : baisser les réglages d'un cran rendrait le jeu plus fluide."),
            Bottleneck.Gpu => (0, "La carte graphique est pleinement utilisée et le jeu est fluide : réglages adaptés."),

            Bottleneck.FrameCap when m.Score < 70 => (0,
                $"FPS plafonnés vers {m.AverageFps:0} alors que l'écran va à {m.RefreshHz} Hz, et la carte graphique n'est occupée que {gpu} % du temps : " +
                "relevez la limite de FPS du jeu ou désactivez la V-Sync."),
            Bottleneck.FrameCap when gpuIdle => (1,
                $"FPS plafonnés (limiteur du jeu ou V-Sync) et carte graphique occupée {gpu} % du temps seulement : vous pouvez monter les réglages d'un cran."),
            Bottleneck.FrameCap => (0, $"FPS plafonnés (limiteur du jeu ou V-Sync), carte graphique occupée {gpu} % du temps : réglages adaptés."),

            Bottleneck.Cpu when m.Score < 70 => (0,
                $"Le processeur limite les FPS (carte graphique occupée {gpu} % du temps seulement) : baisser les graphismes changera peu. " +
                "Réduisez plutôt ce qui charge le processeur (distance d'affichage, foule, physique) et fermez les programmes en arrière-plan."),
            Bottleneck.Cpu when gpuIdle => (1, $"La carte graphique attend le processeur (occupée {gpu} % du temps) : monter les graphismes d'un cran coûtera peu de FPS."),
            Bottleneck.Cpu => (0, "Réglages adaptés à votre PC."),

            // Charge inconnue (capture sans les métriques 2.x) : d'après les FPS seulement.
            _ when m.HasHeadroom => (1, "Il reste de la marge : vous pouvez monter les réglages d'un cran."),
            _ when m.Score < 50 => (-1, "Baissez les réglages d'un cran pour gagner en fluidité."),
            _ => (0, "Réglages adaptés à votre PC."),
        };
    }

    private static int Percent(double? fraction) => (int)Math.Round(100 * (fraction ?? 0));

    /// <summary>Carte d'un niveau de configuration : du même fabricant de préférence, la plus faible citée (une seule suffit).</summary>
    private static GpuPerformance.Match? Pick(RequirementLevel? level, string vendor)
    {
        if (level is null || level.Gpus.Count == 0) return null;
        var sameVendor = level.Gpus.Where(g => g.Vendor == vendor).ToList();
        return (sameVendor.Count > 0 ? sameVendor : level.Gpus).MinBy(g => g.Index);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
