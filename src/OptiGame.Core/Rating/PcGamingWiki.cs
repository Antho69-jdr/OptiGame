using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Rating;

/// <summary>
/// Configurations requises de PCGamingWiki (wiki communautaire, licence CC BY-NC-SA 3.0, à citer) pour les jeux hors Steam :
/// tous magasins confondus (Epic, Battle.net, Riot, RSI…). API MediaWiki publique, seul le nom du jeu est envoyé.
/// Format vérifié le 2026-10-02 sur Star Citizen, Fortnite, Valorant, League of Legends, Genshin Impact et Minecraft
/// (échantillons dans les tests) : modèle <c>{{System requirements}}</c> par système (<c>|OSfamily = Windows</c>), champs
/// <c>|minGPU</c>, <c>|minGPU2</c>…, <c>|recGPU</c>…, <c>|minRAM</c>, <c>|recRAM</c>.
/// <para>Écartés après vérification : IGDB (aucune configuration requise dans son API) ; Can You Run It (ni API ni conditions
/// d'utilisation publiées, recherche faite par un script de la page, et un identifiant erroné affiche un AUTRE jeu sans
/// erreur) ; recherche par nom du magasin Steam (renvoie des packs et DLC : « Overwatch » → « Starter Pack »).</para>
/// </summary>
public static partial class PcGamingWiki
{
    public const string SourceName = "PCGamingWiki";

    private const string Api = "https://www.pcgamingwiki.com/w/api.php";

    public static Uri SearchUri(string gameName) =>
        new($"{Api}?action=opensearch&search={Uri.EscapeDataString(gameName)}&limit=5&namespace=0&format=json");

    public static Uri PageUri(string title) =>
        new($"{Api}?action=parse&page={Uri.EscapeDataString(title)}&prop=wikitext&format=json&formatversion=2&redirects=1");

    public static string PageUrl(string title) => "https://www.pcgamingwiki.com/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));

    /// <summary>
    /// Page du jeu dans une réponse « opensearch » (<c>[recherche, [titres], [descriptions], [adresses]]</c>) : seulement un titre
    /// IDENTIQUE au nom du jeu (sans tenir compte de la casse, de la ponctuation ni des ™ ®). Sinon null : une configuration
    /// requise d'un autre jeu serait pire que pas de note.
    /// </summary>
    public static string? PickTitle(string json, string gameName)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2 || root[1].ValueKind != JsonValueKind.Array) return null;
        var key = Key(gameName);
        if (key.Length == 0) return null;
        foreach (var title in root[1].EnumerateArray())
        {
            if (title.ValueKind == JsonValueKind.String && Key(title.GetString()!) == key) return title.GetString();
        }
        return null;
    }

    /// <summary>« PUBG: BATTLEGROUNDS » et « PUBG: Battlegrounds » → « pubgbattlegrounds ».</summary>
    public static string Key(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));

    /// <summary>Réponse « parse » (formatversion=2) → configuration requise sous Windows ; null si la page n'en a pas.</summary>
    public static SystemRequirements? ParsePage(string json, string title)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("parse", out var parse) || !parse.TryGetProperty("wikitext", out var wikitext) ||
            wikitext.ValueKind != JsonValueKind.String)
        {
            return null; // ex. { "error": { "code": "missingtitle" } }
        }
        var actualTitle = parse.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : title;
        return ParseWikitext(wikitext.GetString()!, PageUrl(actualTitle));
    }

    /// <summary>Premier bloc <c>{{System requirements}}</c> pour Windows (un bloc sans OSfamily est supposé Windows).</summary>
    public static SystemRequirements? ParseWikitext(string wikitext, string? pageUrl = null)
    {
        foreach (var block in Blocks(wikitext))
        {
            if (block.TryGetValue("OSfamily", out var os) && !os.Contains("Windows", StringComparison.OrdinalIgnoreCase)) continue;
            var minimum = Level(block, "min");
            var recommended = Level(block, "rec");
            return minimum is null && recommended is null ? null : new SystemRequirements(minimum, recommended, SourceName, pageUrl);
        }
        return null;
    }

    /// <summary>Champs de chaque bloc : lignes <c>|nom = valeur</c> jusqu'à la fin du modèle ou au bloc suivant.</summary>
    private static IEnumerable<Dictionary<string, string>> Blocks(string wikitext)
    {
        const string start = "{{System requirements";
        var lines = wikitext.Replace("\r", "").Split('\n');
        Dictionary<string, string>? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith(start, StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) yield return current;
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (current is null) continue;
            if (line == "}}")
            {
                yield return current;
                current = null;
                continue;
            }
            if (Field().Match(line) is { Success: true } field) current[field.Groups[1].Value] = Clean(field.Groups[2].Value);
        }
        if (current is not null) yield return current;
    }

    private static RequirementLevel? Level(Dictionary<string, string> block, string prefix)
    {
        var graphics = string.Join(" / ", block
            .Where(f => GpuField().IsMatch(f.Key) && f.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.Value.Length > 0)
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Value));
        int? memory = block.TryGetValue(prefix + "RAM", out var ram) && Memory().Match(ram) is { Success: true } m
            ? Gigabytes(m.Groups[1].Value, m.Groups[2].Value)
            : null;
        return graphics.Length == 0 && memory is null ? null : new RequirementLevel(GpuPerformance.Find(graphics), memory, graphics);
    }

    private static int Gigabytes(string amount, string unit)
    {
        var value = double.Parse(amount.Replace(',', '.'), CultureInfo.InvariantCulture);
        return unit.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? (int)Math.Ceiling(value / 1024) : (int)Math.Ceiling(value);
    }

    /// <summary>Retire les références, modèles imbriqués et liens du wiki ; « &lt;br&gt; » sépare des alternatives.</summary>
    private static string Clean(string value)
    {
        var text = References().Replace(value, " ");
        string previous;
        do
        {
            previous = text;
            text = Templates().Replace(text, " ");
        } while (text != previous);
        text = Links().Replace(text, "$1");
        text = LineBreaks().Replace(text, " / ");
        text = Tags().Replace(text, " ");
        return Spaces().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"^\|\s*(\w+)\s*=\s*(.*)$")]
    private static partial Regex Field();

    [GeneratedRegex(@"^(min|rec)GPU\d*$", RegexOptions.IgnoreCase)]
    private static partial Regex GpuField();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*\+?\s*(GB|MB|GiB|MiB)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Memory();

    [GeneratedRegex(@"<ref[^>]*/>|<ref[^>]*>.*?</ref>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex References();

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex Templates();

    [GeneratedRegex(@"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]")]
    private static partial Regex Links();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
