using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Drivers;

/// <summary>
/// Texte d'un PDF simple, en une seule ligne (espaces regroupés) : flux compressés (FlateDecode) décompressés, chaînes des
/// opérateurs Tj / TJ mises bout à bout. Suffisant pour les notes de version de NVIDIA (polices TrueType en WinAnsiEncoding :
/// texte en clair, vérifié le 2026-10-07 sur les versions 617.14, 616.56 et 591.86) ; PAS un lecteur PDF général (polices CID,
/// chaînes hexadécimales et flux chiffrés ignorés). Le résultat sert à trouver des sections par leur titre.
/// </summary>
public static partial class PdfText
{
    public static string Extract(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var text = new StringBuilder();
        foreach (Match stream in StreamStart().Matches(raw))
        {
            var start = stream.Index + stream.Length;
            var end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;
            string content;
            try
            {
                using var inflate = new ZLibStream(new MemoryStream(pdf, start, end - start), CompressionMode.Decompress);
                using var reader = new StreamReader(inflate, Encoding.Latin1);
                content = reader.ReadToEnd();
            }
            catch (InvalidDataException)
            {
                continue; // flux non compressé ou autre filtre (images…)
            }
            if (!content.Contains("BT", StringComparison.Ordinal)) continue;
            foreach (Match op in TextOperator().Matches(content))
            {
                if (op.Groups["array"].Success)
                {
                    foreach (Match part in LiteralString().Matches(op.Groups["array"].Value)) text.Append(Unescape(part.Groups["s"].Value));
                }
                else
                {
                    text.Append(Unescape(op.Groups["single"].Value));
                }
                text.Append(' '); // les mots sont souvent placés un par un : l'espace en trop disparaît au regroupement
            }
        }
        return Spaces().Replace(text.ToString(), " ").Trim();
    }

    /// <summary>Échappements des chaînes PDF : \n, \(, \\ et codes octaux \ddd (en WinAnsi : \222 = ’, \223 = “…).</summary>
    private static string Unescape(string s)
    {
        var result = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length)
            {
                result.Append(s[i]);
                continue;
            }
            var c = s[++i];
            if (c is >= '0' and <= '7')
            {
                var digits = 1;
                while (digits < 3 && i + digits < s.Length && s[i + digits] is >= '0' and <= '7') digits++;
                result.Append(WinAnsi(Convert.ToInt32(s.Substring(i, digits), 8)));
                i += digits - 1;
                continue;
            }
            result.Append(c switch { 'n' or 'r' or 't' => ' ', _ => c });
        }
        return result.ToString();
    }

    /// <summary>Caractères 0x80-0x9F de WinAnsiEncoding (Windows-1252) qui diffèrent de Latin-1 ; les autres sont identiques.</summary>
    private static char WinAnsi(int code) => code switch
    {
        0x91 or 0x92 => '\'',
        0x93 or 0x94 => '"',
        0x96 or 0x97 => '-',
        0x85 => '…',
        0x99 => '™',
        0x80 => '€',
        _ => (char)code,
    };

    [GeneratedRegex(@"stream\r?\n")]
    private static partial Regex StreamStart();

    [GeneratedRegex(@"\[(?<array>(?:[^\]\\]|\\.)*)\]\s*TJ|\((?<single>(?:[^)\\]|\\.)*)\)\s*Tj")]
    private static partial Regex TextOperator();

    [GeneratedRegex(@"\((?<s>(?:[^)\\]|\\.)*)\)")]
    private static partial Regex LiteralString();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
