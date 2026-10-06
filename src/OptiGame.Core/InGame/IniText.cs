namespace OptiGame.Core.InGame;

/// <summary>
/// Lecture et modification d'UNE valeur d'un fichier .ini, le reste du texte gardé à l'identique (ordre, commentaires, fins de
/// ligne : CRLF dans les GameUserSettings.ini relevés le 2026-10-06). Sections et clés comparées sans tenir compte de la casse,
/// comme Unreal Engine.
/// </summary>
public static class IniText
{
    /// <summary>Valeur de la clé dans la section, ou null si l'une ou l'autre est absente.</summary>
    public static string? Get(string text, string section, string key)
    {
        var lines = Split(text, out _);
        var (start, end) = SectionRange(lines, section);
        if (start < 0) return null;
        var index = KeyLine(lines, start, end, key);
        return index < 0 ? null : lines[index][(lines[index].IndexOf('=') + 1)..].Trim();
    }

    /// <summary>
    /// Texte avec la clé à cette valeur (null = clé retirée). Clé absente : ajoutée à la fin de la section ; section absente :
    /// ajoutée à la fin du fichier.
    /// </summary>
    public static string Set(string text, string section, string key, string? value)
    {
        var lines = Split(text, out var newLine);
        var (start, end) = SectionRange(lines, section);
        if (start < 0)
        {
            if (value is null) return text;
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
            lines.Add("");
            return string.Join(newLine, lines);
        }

        var index = KeyLine(lines, start, end, key);
        if (index >= 0)
        {
            if (value is null) lines.RemoveAt(index);
            else lines[index] = $"{lines[index][..lines[index].IndexOf('=')]}={value}"; // nom de la clé gardé tel quel
        }
        else if (value is not null)
        {
            // Après la dernière ligne non vide de la section (avant la ligne vide qui la sépare de la suivante).
            var insertAt = end;
            while (insertAt > start + 1 && lines[insertAt - 1].Trim().Length == 0) insertAt--;
            lines.Insert(insertAt, $"{key}={value}");
        }
        return string.Join(newLine, lines);
    }

    private static List<string> Split(string text, out string newLine)
    {
        newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return [.. text.Split(newLine)];
    }

    /// <summary>Ligne de l'en-tête de la section et ligne qui suit sa fin (en-tête suivant ou fin du fichier) ; (-1, -1) si absente.</summary>
    private static (int Start, int End) SectionRange(List<string> lines, string section)
    {
        var start = lines.FindIndex(l => IsHeader(l, out var name) && name.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return (-1, -1);
        var end = lines.FindIndex(start + 1, l => IsHeader(l, out _));
        return (start, end < 0 ? lines.Count : end);
    }

    private static bool IsHeader(string line, out string name)
    {
        var trimmed = line.Trim();
        var isHeader = trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']';
        name = isHeader ? trimmed[1..^1] : "";
        return isHeader;
    }

    private static int KeyLine(List<string> lines, int start, int end, string key)
    {
        for (var i = start + 1; i < end; i++)
        {
            var line = lines[i];
            var equal = line.IndexOf('=');
            if (equal > 0 && line[..equal].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}
