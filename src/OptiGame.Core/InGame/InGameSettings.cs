using System.Globalization;
using OptiGame.Core.Rating;
using OptiGame.Core.Text;

namespace OptiGame.Core.InGame;

/// <summary>Mode d'affichage du jeu.</summary>
public enum InGameDisplayMode
{
    Fullscreen,
    Borderless,
    Windowed,
}

/// <summary>
/// Réglages d'un jeu lus dans SES fichiers (lecture seule) : moteurs dont le format est commun à tous les jeux (Unreal Engine :
/// GameUserSettings.ini ; Unity : registre). Champ null = non lu (absent, ou format propre au jeu).
/// </summary>
/// <param name="Preset">Qualité générale, dans l'échelle du jeu (Unreal : 0 Bas, 1 Moyen, 2 Élevé, 3-4 Ultra, sauf jeux connus ;
/// PUBG : « Très bas » à « Ultra » sur 0-4, voir <see cref="UnrealSettings.ScaleOf"/>).</param>
/// <param name="PresetDetail">D'où vient la qualité : « 9 groupes, niveau 1 (Moyen du moteur) ».</param>
/// <param name="RenderScalePercent">Échelle de rendu (Unreal : sg.ResolutionQuality) ; null = 100 % ou inconnue.</param>
/// <param name="FrameLimit">Limite de FPS du moteur ; 0 = aucune.</param>
/// <param name="Upscaler">Upscaling choisi, ex. « DLSS Qualité » ; null = aucun ou inconnu.</param>
public sealed record InGameSettings(
    string Engine,
    string SourceName,
    string SourcePath,
    DateTime SavedAt,
    GraphicsPreset? Preset = null,
    string? PresetDetail = null,
    int? Width = null,
    int? Height = null,
    int? RenderScalePercent = null,
    InGameDisplayMode? DisplayMode = null,
    bool? VSync = null,
    int? FrameLimit = null,
    string? Upscaler = null)
{
    /// <summary>« qualité Moyen, 3440×1440, plein écran fenêtré, V-Sync activée, sans limite de FPS, DLSS Qualité ».</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Preset is { } preset) parts.Add($"qualité {GameRatings.Label(preset)}");
        if (Width is { } w && Height is { } h) parts.Add($"{w}×{h}");
        if (RenderScalePercent is { } scale && scale != 100 && Upscaler is null) parts.Add($"échelle de rendu {scale} %"); // remplacée par l'upscaling
        if (DisplayMode is { } mode)
        {
            parts.Add(mode switch
            {
                InGameDisplayMode.Fullscreen => "plein écran",
                InGameDisplayMode.Borderless => "plein écran fenêtré",
                _ => "fenêtré",
            });
        }
        if (VSync is { } vsync) parts.Add(vsync ? "V-Sync activée" : "V-Sync désactivée");
        if (FrameLimit is { } limit) parts.Add(limit > 0 ? $"limite de {limit} FPS" : "sans limite de FPS");
        if (Upscaler is { } upscaler) parts.Add(upscaler);
        return string.Join(", ", parts);
    }

    /// <summary>« Lu dans GameUserSettings.ini (Unreal Engine), enregistré le 5 oct. 2026 à 23:31 : qualité Moyen, … ».</summary>
    public string Description(DateTime now) =>
        $"Lu dans {SourceName} ({Engine}), enregistré {FrenchText.When(SavedAt, now)} : {Summary()}.";
}

/// <summary>
/// GameUserSettings.ini d'Unreal Engine 4 et 5 (%LocalAppData%\&lt;projet&gt;\Saved\Config\&lt;Windows | WindowsClient |
/// WindowsNoEditor&gt;\). Format vérifié le 2026-10-06 sur PUBG (UE4, WindowsNoEditor) et ARC Raiders (UE5, WindowsClient) :
/// section [ScalabilityGroups] (sg.ViewDistanceQuality=2…, entiers ou « 2.000000 » ; sg.ResolutionQuality = échelle de rendu en
/// %), clés ResolutionSizeX/Y, FullscreenMode (0 plein écran, 1 plein écran fenêtré, 2 fenêtré), bUseVSync, FrameRateLimit
/// (0 = aucune) dans la section des réglages du jeu.
/// </summary>
public static class UnrealSettings
{
    public const string FileName = "GameUserSettings.ini";

    /// <summary>Limite au-delà de laquelle FrameRateLimit ne limite plus rien (PUBG : 1000 = « illimité »).</summary>
    private const double NoLimitAbove = 500;

    /// <summary>Niveaux de qualité d'un jeu : réglage d'OptiGame et nom affiché par le jeu, du niveau 0 au plus haut.</summary>
    public sealed record QualityScale(string? Game, IReadOnlyList<(GraphicsPreset Preset, string Name)> Levels);

    /// <summary>Niveaux standard d'Unreal Engine (Scalability : Low, Medium, High, Epic, Cinematic).</summary>
    public static readonly QualityScale EngineScale = new(null,
    [
        (GraphicsPreset.Low, "Bas"), (GraphicsPreset.Medium, "Moyen"), (GraphicsPreset.High, "Élevé"),
        (GraphicsPreset.Ultra, "Épique"), (GraphicsPreset.Ultra, "Cinématique"),
    ]);

    /// <summary>
    /// Jeux qui nomment autrement les niveaux du moteur, par nom de projet. PUBG (TslGame) : Très bas … Ultra sur 0-4, confirmé
    /// le 2026-10-06 par l'utilisateur (niveau 2 = « Moyen » dans le jeu).
    /// </summary>
    private static readonly Dictionary<string, QualityScale> GameScales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TslGame"] = new("PUBG",
        [
            (GraphicsPreset.Low, "Très bas"), (GraphicsPreset.Low, "Bas"), (GraphicsPreset.Medium, "Moyen"),
            (GraphicsPreset.High, "Élevé"), (GraphicsPreset.Ultra, "Ultra"),
        ]),
    };

    public static QualityScale ScaleOf(string? project) =>
        project is not null && GameScales.TryGetValue(project, out var scale) ? scale : EngineScale;

    /// <param name="project">Nom du projet Unreal (dossier au-dessus de Binaries\Win64) : choisit l'échelle de qualité du jeu.</param>
    public static InGameSettings? Parse(string ini, string path, DateTime savedAt, string? project = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<int>();
        int? renderScale = null;
        var section = "";
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[')
            {
                section = line.Trim('[', ']');
                continue;
            }
            var equal = line.IndexOf('=');
            if (equal <= 0) continue;
            var key = line[..equal].Trim();
            var value = line[(equal + 1)..].Trim();
            if (section.Equals("ScalabilityGroups", StringComparison.OrdinalIgnoreCase) && key.StartsWith("sg.", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var level)) continue;
                if (key.Equals("sg.ResolutionQuality", StringComparison.OrdinalIgnoreCase)) renderScale = (int)Math.Round(level);
                // Paysage : souvent fixé par le jeu, indépendamment du réglage général (ARC Raiders : 3 alors que tout est à 1).
                else if (!key.Equals("sg.LandscapeQuality", StringComparison.OrdinalIgnoreCase)) groups.Add((int)Math.Round(level));
                continue;
            }
            values.TryAdd(key, value); // la 1re occurrence (section des réglages du jeu) l'emporte
        }

        GraphicsPreset? preset = null;
        string? detail = null;
        if (groups.Count >= 4)
        {
            var sorted = groups.Order().ToList();
            var median = sorted[sorted.Count / 2];
            var qualityScale = ScaleOf(project);
            var level = Math.Clamp(median, 0, qualityScale.Levels.Count - 1);
            preset = qualityScale.Levels[level].Preset;
            var where = qualityScale.Game is { } game ? $"« {qualityScale.Levels[level].Name} » dans {game}" : "du moteur, 0 à 4";
            detail = sorted[0] == sorted[^1]
                ? $"{groups.Count} groupes de qualité au niveau {median} ({where})"
                : $"{groups.Count} groupes de qualité, niveaux {sorted[0]} à {sorted[^1]}, médiane {median} ({where})";
        }

        var settings = new InGameSettings("Unreal Engine", FileName, path, savedAt, preset, detail,
            Int("ResolutionSizeX"), Int("ResolutionSizeY"),
            renderScale is { } scale && scale is > 0 and < 100 ? scale : null,
            Int("FullscreenMode") switch { 0 => InGameDisplayMode.Fullscreen, 1 => InGameDisplayMode.Borderless, 2 => InGameDisplayMode.Windowed, _ => null },
            Bool("bUseVSync"),
            Double("FrameRateLimit") is { } limit ? (limit <= 0 || limit > NoLimitAbove ? 0 : (int)Math.Round(limit)) : null,
            Upscaler());
        return settings.Summary().Length == 0 ? null : settings;

        string? Get(string key) => values.TryGetValue(key, out var v) ? v : null;
        double? Double(string key) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        int? Int(string key) => Double(key) is { } d ? (int)Math.Round(d) : null;
        bool? Bool(string key) => Get(key)?.ToLowerInvariant() switch { "true" => true, "false" => false, _ => null };

        // Clés de jeux Unreal qui nomment la méthode puis son mode : ARC Raiders = ResolutionScalingMethod=DLSS + DLSSMode=Quality ;
        // PUBG = UpscalingMethod=None (+ DLSSQualityOption=Auto).
        string? Upscaler()
        {
            var method = Get("ResolutionScalingMethod") ?? Get("UpscalingMethod");
            if (method is null || !(method.Equals("DLSS", StringComparison.OrdinalIgnoreCase) || method.Equals("FSR", StringComparison.OrdinalIgnoreCase)
                || method.Equals("XeSS", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            var mode = Get(method + "Mode") ?? Get(method + "QualityOption");
            return mode is null ? method.ToUpperInvariant() : $"{(method.Equals("XeSS", StringComparison.OrdinalIgnoreCase) ? "XeSS" : method.ToUpperInvariant())} {ModeLabel(mode)}";
        }
    }

    private static string ModeLabel(string mode) => mode.ToLowerInvariant() switch
    {
        "quality" => "Qualité",
        "balanced" => "Équilibré",
        "performance" => "Performance",
        "ultraperformance" => "Ultra performance",
        "ultraquality" => "Ultra qualité",
        "dlaa" or "nativeaa" => "anticrénelage natif",
        "auto" => "automatique",
        _ => mode,
    };
}

/// <summary>
/// Réglages d'écran d'un jeu Unity : registre HKCU\Software\&lt;éditeur&gt;\&lt;jeu&gt; (noms lus dans &lt;exe&gt;_Data\app.info,
/// 1re ligne = éditeur, 2e = jeu). Vérifié le 2026-10-06 sur Void Crew : « Screenmanager Resolution Width_h182942802 » (DWORD),
/// « … Height_h2627697771 », « Screenmanager Fullscreen mode_h3630240806 » (0 exclusif, 1 fenêtré sans bordure, 2 fenêtre
/// agrandie, 3 fenêtré). « UnityGraphicsQuality » n'est PAS lu : l'index d'un niveau défini par chaque jeu (Void Crew : 1 alors
/// que le jeu est en Ultra, ses vrais réglages sont dans son propre Settings.json).
/// </summary>
public static class UnitySettings
{
    public const string AppInfoFile = "app.info";

    public static (string Company, string Product)? ParseAppInfo(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return lines.Count >= 2 ? (lines[0], lines[1]) : null;
    }

    /// <param name="values">Valeurs de la clé de registre du jeu (nom → valeur DWORD).</param>
    public static InGameSettings? FromRegistry(IReadOnlyDictionary<string, int> values, string keyPath, DateTime savedAt)
    {
        int? Value(string prefix) =>
            values.FirstOrDefault(v => v.Key.StartsWith(prefix + "_h", StringComparison.Ordinal)) is { Key: not null } found ? found.Value : null;

        var settings = new InGameSettings("Unity", "le registre de Windows", keyPath, savedAt,
            Width: Value("Screenmanager Resolution Width"),
            Height: Value("Screenmanager Resolution Height"),
            DisplayMode: Value("Screenmanager Fullscreen mode") switch
            {
                0 => InGameDisplayMode.Fullscreen,
                1 => InGameDisplayMode.Borderless,
                2 or 3 => InGameDisplayMode.Windowed,
                _ => null,
            });
        return settings.Summary().Length == 0 ? null : settings;
    }
}
