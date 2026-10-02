using System.Text.RegularExpressions;

namespace OptiGame.Core.Rating;

/// <summary>
/// Indice de performances relatives des cartes graphiques de bureau (jeu en 1080p, GTX 1060 6 Go = 100).
/// ESTIMATION : ordres de grandeur tirés des comparatifs publics de performances relatives, arrondis ; sert à comparer une
/// carte à la configuration requise d'un jeu, jamais à promettre des FPS. Une carte absente de la table n'est pas devinée.
/// </summary>
public static partial class GpuPerformance
{
    private static readonly Dictionary<string, int> Index = new(StringComparer.OrdinalIgnoreCase)
    {
        // NVIDIA
        ["GT 1030"] = 30, ["GTX 650"] = 25, ["GTX 650 Ti"] = 33, ["GTX 660"] = 45, ["GTX 660 Ti"] = 52, ["GTX 670"] = 58, ["GTX 680"] = 63,
        ["GTX 750"] = 33, ["GTX 750 Ti"] = 40, ["GTX 760"] = 52, ["GTX 770"] = 63, ["GTX 780"] = 72, ["GTX 780 Ti"] = 85,
        ["GTX 950"] = 50, ["GTX 960"] = 60, ["GTX 970"] = 85, ["GTX 980"] = 98, ["GTX 980 Ti"] = 120,
        ["GTX 1050"] = 50, ["GTX 1050 Ti"] = 60, ["GTX 1060"] = 100, ["GTX 1070"] = 130, ["GTX 1070 Ti"] = 145, ["GTX 1080"] = 160, ["GTX 1080 Ti"] = 205,
        ["GTX 1630"] = 45, ["GTX 1650"] = 72, ["GTX 1650 Super"] = 92, ["GTX 1660"] = 103, ["GTX 1660 Super"] = 118, ["GTX 1660 Ti"] = 118,
        ["RTX 2060"] = 140, ["RTX 2060 Super"] = 158, ["RTX 2070"] = 165, ["RTX 2070 Super"] = 185, ["RTX 2080"] = 200, ["RTX 2080 Super"] = 210, ["RTX 2080 Ti"] = 245,
        ["RTX 3050"] = 115, ["RTX 3060"] = 165, ["RTX 3060 Ti"] = 205, ["RTX 3070"] = 235, ["RTX 3070 Ti"] = 250, ["RTX 3080"] = 290, ["RTX 3080 Ti"] = 315,
        ["RTX 3090"] = 325, ["RTX 3090 Ti"] = 350,
        ["RTX 4060"] = 185, ["RTX 4060 Ti"] = 220, ["RTX 4070"] = 290, ["RTX 4070 Super"] = 330, ["RTX 4070 Ti"] = 350, ["RTX 4070 Ti Super"] = 380,
        ["RTX 4080"] = 420, ["RTX 4080 Super"] = 430, ["RTX 4090"] = 520,
        ["RTX 5060"] = 220, ["RTX 5060 Ti"] = 250, ["RTX 5070"] = 340, ["RTX 5070 Ti"] = 410, ["RTX 5080"] = 470, ["RTX 5090"] = 620,
        // AMD
        ["HD 7750"] = 30, ["HD 7770"] = 38, ["HD 7850"] = 55, ["HD 7870"] = 63, ["HD 7950"] = 70, ["HD 7970"] = 80,
        ["R7 260X"] = 40, ["R7 370"] = 52, ["R9 270X"] = 60, ["R9 280X"] = 80, ["R9 290"] = 95, ["R9 290X"] = 100,
        ["R9 380"] = 70, ["R9 380X"] = 75, ["R9 390"] = 100, ["R9 390X"] = 105,
        ["RX 460"] = 45, ["RX 470"] = 85, ["RX 480"] = 95, ["RX 550"] = 30, ["RX 560"] = 50, ["RX 570"] = 88, ["RX 580"] = 100, ["RX 590"] = 108,
        ["RX Vega 56"] = 130, ["RX Vega 64"] = 145,
        ["RX 5500 XT"] = 95, ["RX 5600 XT"] = 135, ["RX 5700"] = 150, ["RX 5700 XT"] = 165,
        ["RX 6400"] = 60, ["RX 6500 XT"] = 70, ["RX 6600"] = 145, ["RX 6600 XT"] = 165, ["RX 6650 XT"] = 175, ["RX 6700 XT"] = 205, ["RX 6750 XT"] = 215,
        ["RX 6800"] = 255, ["RX 6800 XT"] = 290, ["RX 6900 XT"] = 310, ["RX 6950 XT"] = 325,
        ["RX 7600"] = 175, ["RX 7600 XT"] = 180, ["RX 7700 XT"] = 250, ["RX 7800 XT"] = 290, ["RX 7900 GRE"] = 320, ["RX 7900 XT"] = 370, ["RX 7900 XTX"] = 420,
        ["RX 9060 XT"] = 230, ["RX 9070"] = 360, ["RX 9070 XT"] = 400,
        // Intel
        ["Iris Xe"] = 25, ["Arc A380"] = 55, ["Arc A750"] = 140, ["Arc A770"] = 150, ["Arc B580"] = 180,
    };

    /// <summary>Séries citées sans modèle (« GTX 600 series », « HD 7000 series ») : la carte la plus faible de la série.</summary>
    private static readonly Dictionary<string, int> Series = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GTX 600"] = 25, ["GTX 700"] = 33, ["GTX 900"] = 50, ["GTX 10"] = 50, ["GTX 16"] = 45, ["RTX 20"] = 140, ["RTX 30"] = 115, ["RTX 40"] = 185,
        ["HD 7000"] = 30, ["R7"] = 40, ["R9"] = 60, ["RX 400"] = 45, ["RX 500"] = 30, ["RX 5000"] = 95, ["RX 6000"] = 60, ["RX 7000"] = 175,
    };

    public static IReadOnlyCollection<string> KnownModels => Index.Keys;

    /// <summary>Carte reconnue dans un texte (requis d'un jeu ou nom de la carte installée).</summary>
    public sealed record Match(string Model, int Index, string Vendor);

    /// <summary>Toutes les cartes reconnues dans un texte, dans l'ordre (alternatives « / », « , », « or »).</summary>
    public static IReadOnlyList<Match> Find(string text)
    {
        var clean = Clean(text);
        var found = new List<Match>();
        foreach (System.Text.RegularExpressions.Match m in SeriesPattern().Matches(clean))
        {
            var key = $"{m.Groups[1].Value} {m.Groups[2].Value}".ToUpperInvariant();
            if (Series.TryGetValue(key, out var index)) found.Add(new Match($"{key} (série)", index, VendorOf(key)));
        }
        foreach (System.Text.RegularExpressions.Match m in ModelPattern().Matches(clean))
        {
            var normalized = Normalize(m.Value);
            if (!Index.TryGetValue(normalized, out var index)) continue;
            var model = Index.Keys.First(k => k.Equals(normalized, StringComparison.OrdinalIgnoreCase)); // graphie de la table
            if (found.All(f => f.Model != model)) found.Add(new Match(model, index, VendorOf(model)));
        }
        return found;
    }

    /// <summary>Indice de la carte installée (ex. « NVIDIA GeForce RTX 3070 » → 235), ou null si absente de la table.</summary>
    public static Match? Identify(string gpuName) => Find(gpuName).FirstOrDefault(m => !m.Model.EndsWith("(série)", StringComparison.Ordinal));

    private static string VendorOf(string model) =>
        model.StartsWith("GT", StringComparison.OrdinalIgnoreCase) || model.StartsWith("RTX", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
        : model.StartsWith("Arc", StringComparison.OrdinalIgnoreCase) || model.StartsWith("Iris", StringComparison.OrdinalIgnoreCase) ? "Intel"
        : "AMD";

    private static string Clean(string text) =>
        Spaces().Replace(text.Replace("®", " ").Replace("™", " ").Replace("(R)", " ").Replace("(TM)", " "), " ");

    /// <summary>« RTX3070TI » → « RTX 3070 Ti » ; « rx 6700xt » → « RX 6700 XT ».</summary>
    private static string Normalize(string raw)
    {
        var m = ModelParts().Match(raw.ToUpperInvariant());
        if (!m.Success) return raw;
        var family = m.Groups[1].Value.Replace("ARC", "Arc").Replace("IRIS", "Iris").Replace("VEGA", "Vega");
        family = Spaces().Replace(family, " ").Trim();
        var number = m.Groups[2].Value;
        var suffix = m.Groups[3].Value.Replace(" ", "") switch
        {
            "TI" => " Ti",
            "TISUPER" => " Ti Super",
            "SUPER" => " Super",
            "XT" => " XT",
            "XTX" => " XTX",
            "GRE" => " GRE",
            "X" => "X",
            _ => "",
        };
        return $"{family} {number}{suffix}".Trim();
    }

    [GeneratedRegex(@"\b(GTX|RTX|HD|RX)\s?(\d{1,2}0{1,3})\s+series\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPattern();

    [GeneratedRegex(@"\b(?:GTX|RTX|GT|RX\s?Vega|RX|R[79]|HD|Arc|Iris)\s?[A-Z]?\d{2,4}\s?(?:Ti\s?Super|Ti|Super|XTX|XT|GRE|X\b)?|\bIris\s?Xe\b", RegexOptions.IgnoreCase)]
    private static partial Regex ModelPattern();

    [GeneratedRegex(@"^(GTX|RTX|GT|RX\s?VEGA|RX|R[79]|HD|ARC|IRIS)\s?([AB]?\d{2,4}|XE)\s?(TI\s?SUPER|TI|SUPER|XTX|XT|GRE|X)?")]
    private static partial Regex ModelParts();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
