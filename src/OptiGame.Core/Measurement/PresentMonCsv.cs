using System.Globalization;

namespace OptiGame.Core.Measurement;

/// <summary>Une image (un appel à Present) d'une capture PresentMon.</summary>
public sealed record FrameSample(string Application, int ProcessId, string SwapChain, double MsBetweenPresents, double? MsBetweenDisplayChange);

/// <summary>
/// Lecture d'un CSV PresentMon. Les colonnes sont repérées par leur nom dans l'en-tête, ce qui couvre les métriques
/// 1.x et 2.x (MsBetweenPresents existe dans les deux). Les valeurs « NA » (image non affichée…) deviennent null.
/// Documentation vérifiée : README-ConsoleApplication.md de PresentMon v2.6.0.
/// </summary>
public static class PresentMonCsv
{
    public static IReadOnlyList<FrameSample> Parse(TextReader reader)
    {
        var header = reader.ReadLine() ?? throw new FormatException("Fichier CSV vide.");
        var columns = SplitLine(header).Select(c => c.Trim()).ToList();
        int Index(string name) => columns.FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

        var betweenPresents = Index("MsBetweenPresents");
        if (betweenPresents < 0)
        {
            throw new FormatException("Colonne MsBetweenPresents absente : ce fichier n'est pas un CSV PresentMon reconnu.");
        }
        var application = Index("Application");
        var processId = Index("ProcessID");
        var swapChain = Index("SwapChainAddress");
        var betweenDisplay = Index("MsBetweenDisplayChange");

        var frames = new List<FrameSample>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var fields = SplitLine(line);
            if (ParseDouble(Field(fields, betweenPresents)) is not { } frameTime) continue;

            frames.Add(new FrameSample(
                Field(fields, application) ?? "",
                int.TryParse(Field(fields, processId), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? pid : 0,
                Field(fields, swapChain) ?? "",
                frameTime,
                ParseDouble(Field(fields, betweenDisplay))));
        }
        return frames;
    }

    /// <summary>
    /// Images de la chaîne d'affichage principale : celle qui a le plus d'images. Les overlays (Discord, Steam…)
    /// ont leur propre chaîne et fausseraient les statistiques.
    /// </summary>
    public static IReadOnlyList<FrameSample> MainSwapChain(IReadOnlyList<FrameSample> frames) =>
        frames.GroupBy(f => (f.ProcessId, f.SwapChain))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.ToList() ?? [];

    private static string? Field(IReadOnlyList<string> fields, int index) =>
        index >= 0 && index < fields.Count ? fields[index].Trim() : null;

    private static double? ParseDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;

    /// <summary>Découpage CSV avec prise en charge des champs entre guillemets.</summary>
    private static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
