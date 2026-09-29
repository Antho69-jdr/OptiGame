using System.Text;

namespace OptiGame.Core.Library;

/// <summary>Nœud KeyValues de Valve (format des fichiers .vdf / .acf de Steam).</summary>
public sealed class VdfNode
{
    public string? Value { get; init; }

    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.GetValueOrDefault(key);

    public string? GetString(string key) => this[key]?.Value;
}

/// <summary>Analyseur minimal du format KeyValues texte : "clé" "valeur" ou "clé" { … }.</summary>
public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        var position = 0;
        var root = new VdfNode();
        ParseChildren(text, ref position, root, expectClosingBrace: false);
        return root;
    }

    private static void ParseChildren(string text, ref int position, VdfNode parent, bool expectClosingBrace)
    {
        while (true)
        {
            SkipWhitespaceAndComments(text, ref position);
            if (position >= text.Length)
            {
                if (expectClosingBrace) throw new FormatException("Accolade fermante manquante.");
                return;
            }

            if (text[position] == '}')
            {
                if (!expectClosingBrace) throw new FormatException($"Accolade fermante inattendue (position {position}).");
                position++;
                return;
            }

            var key = ReadString(text, ref position);
            SkipWhitespaceAndComments(text, ref position);
            if (position < text.Length && text[position] == '{')
            {
                position++;
                var child = new VdfNode();
                ParseChildren(text, ref position, child, expectClosingBrace: true);
                parent.Children[key] = child;
            }
            else
            {
                parent.Children[key] = new VdfNode { Value = ReadString(text, ref position) };
            }
        }
    }

    private static string ReadString(string text, ref int position)
    {
        if (position >= text.Length || text[position] != '"')
        {
            throw new FormatException($"Chaîne entre guillemets attendue (position {position}).");
        }

        position++;
        var builder = new StringBuilder();
        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
                builder.Append(text[position] switch { 'n' => '\n', 't' => '\t', var c => c });
            }
            else
            {
                builder.Append(text[position]);
            }
            position++;
        }

        if (position >= text.Length) throw new FormatException("Guillemet fermant manquant.");
        position++;
        return builder.ToString();
    }

    private static void SkipWhitespaceAndComments(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n') position++;
            }
            else
            {
                return;
            }
        }
    }
}
