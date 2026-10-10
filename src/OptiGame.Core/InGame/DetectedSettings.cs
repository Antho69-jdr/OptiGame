using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptiGame.Core.InGame;

/// <summary>
/// Réglages d'un jeu SANS définition (ni moteur connu) : fichier trouvé par Platform (GameConfigFinder), lu dans n'importe quel
/// format courant (INI, JSON, KeyValues de Valve, .cfg « clé valeur »), puis interprété avec prudence. Seul ce que le NOM de la clé
/// et la valeur disent sans ambiguïté est interprété : dimensions de l'écran, V-Sync en vrai / faux (ou 0 / 1 si la clé dit
/// « activée »), limite de FPS en nombre d'images, plein écran en vrai / faux. Tout le reste (qualité, modes numérotés) est montré
/// tel quel, jamais deviné : un même nom peut vouloir dire autre chose d'un jeu à l'autre (Void Crew : « VSync 0 » = V-Sync
/// ACTIVÉE). Vérifié le 2026-10-10 sur les fichiers réels d'Overwatch, Portal 2 et Scrap Mechanic.
/// </summary>
public static partial class DetectedSettings
{
    public const string Engine = "détection automatique";

    /// <summary>Paires clé / valeur d'un fichier de réglages, clés telles qu'écrites (section ou chemin JSON préfixé par un point).</summary>
    public static IReadOnlyList<(string Key, string Value)> Parse(string text)
    {
        var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var pairs = new List<(string, string)>();
                Flatten(document.RootElement, "", pairs, 0);
                return pairs;
            }
            catch (JsonException)
            {
                // Pas du JSON (ou KeyValues de Valve, qui commence aussi par une accolade dans certains fichiers) : lignes.
            }
        }
        var result = new List<(string, string)>();
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line[0] is ';' or '#' or '{' or '}') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }
            // KeyValues de Valve et .cfg de Source : « "clé"  "valeur" » / « clé "valeur" » ; INI : « clé = valeur » / « clé: valeur ».
            if (KeyValueLine().Match(line) is { Success: true } kv)
            {
                result.Add((Prefixed(section, kv.Groups["key"].Value), kv.Groups["value"].Value));
                continue;
            }
            if (AssignmentLine().Match(line) is { Success: true } assignment)
            {
                result.Add((Prefixed(section, assignment.Groups["key"].Value.Trim()), assignment.Groups["value"].Value.Trim().Trim('"', '\'')));
            }
        }
        return result;
    }

    private static string Prefixed(string section, string key) => section.Length == 0 ? key : $"{section}.{key}";

    private static void Flatten(JsonElement element, string path, List<(string, string)> pairs, int depth)
    {
        if (depth > 6 || pairs.Count > 2000) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Flatten(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", pairs, depth + 1);
                break;
            case JsonValueKind.Array:
                break; // listes (touches, sauvegardes…) : pas des réglages graphiques
            case JsonValueKind.String:
                pairs.Add((path, element.GetString() ?? ""));
                break;
            default:
                pairs.Add((path, element.GetRawText()));
                break;
        }
    }

    [GeneratedRegex("""^"?(?<key>[A-Za-z_][\w.\-]*)"?\s+"(?<value>[^"]*)"\s*(//.*)?$""")]
    private static partial Regex KeyValueLine();

    [GeneratedRegex("""^"?(?<key>[A-Za-z_][\w.\- ]*?)"?\s*[=:]\s*(?<value>.*)$""")]
    private static partial Regex AssignmentLine();

    /// <summary>Clé ramenée à son dernier segment, en minuscules, sans séparateurs ni préfixes courants (« setting.defaultres » → « defaultres »).</summary>
    public static string Normalize(string key)
    {
        var last = key[(key.LastIndexOfAny(['.', '/', ':']) + 1)..];
        var simple = string.Concat(last.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return simple.StartsWith("setting", StringComparison.Ordinal) && simple.Length > 7 ? simple[7..] : simple;
    }

    private static readonly HashSet<string> WidthKeys =
        ["width", "resolutionwidth", "resolutionsizex", "screenwidth", "fullscreenwidth", "resx", "resolutionx", "defaultres", "displaywidth", "screenresolutionwidth"];

    private static readonly HashSet<string> HeightKeys =
        ["height", "resolutionheight", "resolutionsizey", "screenheight", "fullscreenheight", "resy", "resolutiony", "defaultresheight", "displayheight", "screenresolutionheight"];

    private static readonly HashSet<string> FrameLimitKeys =
        ["framelimit", "frameratelimit", "frameratecap", "fpslimit", "fpscap", "maxfps", "fpsmax", "maxframerate", "framecap", "framespersecondlimit"];

    /// <summary>Clés dont un 0 / 1 est sans ambiguïté un interrupteur de V-Sync (le nom dit « activée », ou variable documentée de Source).</summary>
    private static readonly HashSet<string> VSyncSwitchKeys =
        ["verticalsyncenabled", "vsyncenabled", "enablevsync", "usevsync", "busevsync", "matvsync", "waitforvsync"];

    /// <summary>Mots des réglages graphiques : seules ces clés sont montrées (pas le son, les touches, les sauvegardes).</summary>
    [GeneratedRegex("quality|shadow|texture|resolution|vsync|verticalsync|fullscreen|windowmode|windowed|borderless|nowindowborder|displaymode|renderscale|resolutionscale|upscal|dlss|fsr|xess|antialias|^aa$|fxaa|^taa|aniso|ssao|ambientocclusion|reflection|foliage|drawdistance|viewdistance|lodbias|lodlevel|effects|particle|lighting|fog|volumetric|godray|tessel|refresh|motionblur|bloom|^dof$|depthoffield|^hdr|raytrac|gpulevel|cpulevel|detail|sharpen|^fov$|fieldofview|framerate|fpslimit|fpscap|maxfps", RegexOptions.IgnoreCase)]
    private static partial Regex GraphicsWord();

    /// <summary>Pas des réglages d'image : son, versions, superpositions.</summary>
    [GeneratedRegex("sound|audio|volume|voice|music|version|overlay|subtitle", RegexOptions.IgnoreCase)]
    private static partial Regex NotGraphics();

    private static bool IsGraphics(string key)
    {
        var k = Normalize(key);
        return !NotGraphics().IsMatch(k) &&
               (GraphicsWord().IsMatch(k) || WidthKeys.Contains(k) || HeightKeys.Contains(k) || FrameLimitKeys.Contains(k) || VSyncSwitchKeys.Contains(k));
    }

    /// <summary>Nombre de réglages graphiques DIFFÉRENTS (une clé répétée ne compte qu'une fois).</summary>
    public static int Score(IReadOnlyList<(string Key, string Value)> pairs) =>
        pairs.Where(p => IsGraphics(p.Key)).Select(p => Normalize(p.Key)).Distinct().Count();

    /// <summary>
    /// Classement des fichiers trouvés : nombre de réglages graphiques, favorisé quand ils forment l'essentiel du fichier (Portal 2 :
    /// video.txt, 10 réglages graphiques sur 17, l'emporte sur config.cfg, quelques-uns parmi 300 variables).
    /// </summary>
    public static double Rank(IReadOnlyList<(string Key, string Value)> pairs)
    {
        var graphics = Score(pairs);
        var all = Math.Max(1, pairs.Select(p => Normalize(p.Key)).Distinct().Count());
        return graphics * (0.5 + (double)graphics / all);
    }

    /// <summary>Réglages interprétés, ou null si le fichier ne parle pas d'affichage (moins de 3 réglages graphiques).</summary>
    public static InGameSettings? Interpret(IReadOnlyList<(string Key, string Value)> pairs, string sourceName, string sourcePath, DateTime savedAt)
    {
        var graphics = pairs.Where(p => IsGraphics(p.Key)).ToList();
        if (Score(pairs) < 3) return null;

        int? width = null, height = null, frameLimit = null;
        bool? vsync = null;
        InGameDisplayMode? mode = null;
        foreach (var (key, value) in graphics)
        {
            var k = Normalize(key);
            if (width is null && WidthKeys.Contains(k) && Dimension(value) is { } w) width = w;
            else if (height is null && HeightKeys.Contains(k) && Dimension(value) is { } h) height = h;
            else if (width is null && k is "resolution" or "screenresolution" && ResolutionValue().Match(value) is { Success: true } r)
            {
                (width, height) = (int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture));
            }
            else if (vsync is null && (k.Contains("vsync", StringComparison.Ordinal) || k.Contains("verticalsync", StringComparison.Ordinal)))
            {
                vsync = Switch(value, numericAllowed: VSyncSwitchKeys.Contains(k));
            }
            else if (frameLimit is null && FrameLimitKeys.Contains(k) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps)
                     && fps >= 20 && fps <= 500)
            {
                frameLimit = (int)Math.Round(fps);
            }
            else if (mode is null && k == "fullscreen" && Switch(value, numericAllowed: true) == true)
            {
                mode = InGameDisplayMode.Fullscreen; // « non » ne dit pas si c'est fenêtré ou sans bordure : laissé inconnu
            }
        }
        if (width is null != height is null) (width, height) = (null, null);

        var options = graphics.Take(40).Select(p => new InGameOption(p.Key, p.Value)).ToList();
        return new InGameSettings(Engine, sourceName, sourcePath, savedAt, Width: width, Height: height, DisplayMode: mode, VSync: vsync,
            FrameLimit: frameLimit, Options: options);
    }

    [GeneratedRegex(@"^\s*(\d{3,5})\s*[x×*,]\s*(\d{3,5})")]
    private static partial Regex ResolutionValue();

    private static int? Dimension(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 480 && v <= 16384 && v == Math.Floor(v) ? (int)v : null;

    /// <summary>Vrai / faux écrit en toutes lettres ; 0 / 1 seulement si la clé est un interrupteur sans ambiguïté.</summary>
    private static bool? Switch(string value, bool numericAllowed) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "on" or "yes" => true,
        "false" or "off" or "no" => false,
        "1" when numericAllowed => true,
        "0" when numericAllowed => false,
        _ => null,
    };
}
