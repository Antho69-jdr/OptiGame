using System.Globalization;
using System.Text;

namespace OptiGame.Core.Text;

/// <summary>
/// Règles de typographie française de l'interface : accord réel des pluriels (jamais « (s) ») et espaces insécables.
/// </summary>
public static class FrenchText
{
    /// <summary>Espace fine insécable (U+202F) : avant « ; ? ! » et « % », à l'intérieur des guillemets « ».</summary>
    public const char NarrowNoBreakSpace = '\u202F';

    /// <summary>Espace insécable (U+00A0) : avant « : ».</summary>
    public const char NoBreakSpace = '\u00A0';

    /// <summary>« 0 jeu », « 1 jeu », « 3 jeux » (en français, 0 et 1 sont au singulier).</summary>
    public static string Count(int count, string singular, string plural) =>
        $"{count}{NoBreakSpace}{(Math.Abs(count) < 2 ? singular : plural)}";

    /// <summary>Le mot seul, accordé : Agree(3, "appliquée", "appliquées") = « appliquées ».</summary>
    public static string Agree(int count, string singular, string plural) => Math.Abs(count) < 2 ? singular : plural;

    /// <summary>
    /// Remplace les espaces ordinaires par les espaces insécables de la typographie française : avant « : » (insécable),
    /// avant « ; ? ! % » et à l'intérieur des « » (fine insécable). Ne touche ni aux chemins (« C:\ »), ni aux heures (« 14:32 »),
    /// ni aux adresses (« https:// ») : seule une espace DÉJÀ présente est remplacée.
    /// </summary>
    public static string Typeset(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ' ')
            {
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                var previous = i > 0 ? text[i - 1] : '\0';
                if (next == ':') { builder.Append(NoBreakSpace); continue; }
                if (next is ';' or '?' or '!' or '%' or '»' || previous == '«') { builder.Append(NarrowNoBreakSpace); continue; }
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Le format de date de toute l'interface : « 22 sept. 2026 » (mois abrégé à la française).</summary>
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", French);

    /// <summary>Partie date d'une date et heure (déjà en heure locale) : « 22 sept. 2026 ».</summary>
    public static string Date(DateTime date) => date.ToString("d MMM yyyy", French);

    /// <summary>« 22 sept. 2026 à 14:32 », « 3 mars 2026 à 9:05 » (heure locale).</summary>
    public static string DateAndTime(DateTime local) => $"{Date(local)} à {local.ToString("H:mm", French)}";

    /// <summary>
    /// Moment récent, relatif quand c'est plus parlant : « aujourd'hui à 14:32 », « hier à 9:05 », sinon « le 22 sept. 2026
    /// à 14:32 ». S'écrit après un participe : « Appliquée aujourd'hui à 14:32 », « Appliquée le 22 sept. 2026 à 14:32 ».
    /// </summary>
    public static string When(DateTime local, DateTime nowLocal)
    {
        var time = local.ToString("H:mm", French);
        return (nowLocal.Date - local.Date).Days switch
        {
            0 => $"aujourd'hui à {time}",
            1 => $"hier à {time}",
            _ => $"le {Date(local)} à {time}",
        };
    }
}
