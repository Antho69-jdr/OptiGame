namespace OptiGame.Core.Settings;

/// <summary>
/// Manipule les valeurs de UserGpuPreferences, au format <c>Clé=Valeur;Clé=Valeur;</c>.
/// On ne modifie que la clé voulue et on conserve les autres (ex. AutoHDREnable) et leur ordre.
/// </summary>
public static class GpuPreferenceString
{
    public const string GpuPreferenceKey = "GpuPreference";

    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? value)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return pairs;
        }

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            pairs.Add(eq < 0
                ? new(part, "")
                : new(part[..eq].Trim(), part[(eq + 1)..].Trim()));
        }
        return pairs;
    }

    public static string? Get(string? value, string key) =>
        Parse(value).Where(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value).LastOrDefault();

    public static string Set(string? value, string key, string newValue)
    {
        var result = new List<KeyValuePair<string, string>>();
        var replaced = false;
        foreach (var pair in Parse(value))
        {
            if (!pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(pair);
            }
            else if (!replaced)
            {
                result.Add(new(pair.Key, newValue));
                replaced = true;
            }
            // Doublons éventuels de la clé : supprimés.
        }

        if (!replaced)
        {
            result.Add(new(key, newValue));
        }

        return Format(result);
    }

    public static string Format(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
}
