using System.Net;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Drivers;

/// <summary>
/// Notes de version d'un pilote NVIDIA. Deux sources, formats relevés le 2026-10-07 :
/// <list type="bullet">
/// <item>Réponse du service de recherche (<see cref="NvidiaDrivers.ParseLookup"/>) : <c>ReleaseNotes</c> (HTML encodé URL) =
/// titre « Game Ready for … », listes « Fixed Gaming Bugs » / « Fixed General Bugs » ; <c>OtherNotes</c> = lien vers le PDF des
/// notes de version (« …-release-notes.pdf »).</item>
/// <item>PDF des notes de version (617.14, 616.56, 591.86 vérifiés) : section « Open Issues in Version X WHQL », problèmes
/// ENCORE OUVERTS après « &gt; », jusqu'au numéro de la section suivante (« 3.3 Issues Not Caused by NVIDIA Drivers ») ;
/// pieds de page « RN-08399-617.14_v01 | 15 Release 615 Driver for Windows, Version 617.14 » intercalés.</item>
/// </list>
/// </summary>
public static partial class NvidiaReleaseNotes
{
    /// <summary>Titre en gras du début des notes : « Game Ready for CONTROL Resonant, … &amp; AION 2 » ; null s'il n'y en a pas.</summary>
    public static string? GameReadyTitle(string releaseNotesHtml) =>
        GameReady().Match(releaseNotesHtml) is { Success: true } m ? Clean(m.Groups[1].Value) : null;

    /// <summary>Problèmes corrigés (listes « Fixed … Bugs ») des notes du service de recherche, numéros de bug retirés.</summary>
    public static IReadOnlyList<string> FixedIssues(string releaseNotesHtml)
    {
        var fixes = new List<string>();
        foreach (Match section in FixedSection().Matches(releaseNotesHtml))
        {
            fixes.AddRange(ListItem().Matches(section.Groups["list"].Value).Select(i => Clean(BugNumber().Replace(i.Groups[1].Value, ""))));
        }
        return fixes.Where(f => f.Length > 0).ToList();
    }

    /// <summary>Lien du PDF des notes de version (dans OtherNotes) ; null s'il n'y en a pas.</summary>
    public static Uri? PdfUri(string otherNotesHtml) =>
        PdfLink().Match(otherNotesHtml) is { Success: true } m && Uri.TryCreate(m.Groups[1].Value, UriKind.Absolute, out var uri) ? uri : null;

    /// <summary>
    /// Problèmes encore ouverts, d'après le texte du PDF (<see cref="PdfText.Extract"/>) ; null si la section est introuvable
    /// (format changé : on ne devine pas). Liste vide = NVIDIA n'en signale aucun.
    /// </summary>
    public static IReadOnlyList<string>? OpenIssues(string pdfText, string version)
    {
        var heading = new Regex($@"Open Issues in Version {Regex.Escape(version)}\b");
        var headings = heading.Matches(pdfText);
        if (headings.Count == 0) return null;
        var start = headings[^1].Index; // le dernier : le titre de la section, après la table des matières et le sommaire du chapitre
        var end = pdfText.IndexOf("Issues Not Caused by NVIDIA", start, StringComparison.Ordinal);
        if (end < 0) return null;

        var section = Footer().Replace(pdfText[start..end], " ");
        section = NextSectionNumber().Replace(section, "");
        return section.Split(" > ").Skip(1).Select(Clean).Where(i => i.Length > 0).ToList();
    }

    private static string Clean(string html) => Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    [GeneratedRegex(@"<b>\s*Game Ready for\s+(.+?)</b>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex GameReady();

    [GeneratedRegex(@"<b>\s*Fixed [A-Za-z ]*Bugs\s*</b>(?<list>.*?)</ul>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FixedSection();

    [GeneratedRegex(@"<li>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"\s*\[\d{6,8}\]")]
    private static partial Regex BugNumber();

    [GeneratedRegex(@"href=""(https://[^""]+release-notes\.pdf)""", RegexOptions.IgnoreCase)]
    private static partial Regex PdfLink();

    // Espaces variables selon la version : « 617.14_ v01 », « 591.86 _ v01 ».
    [GeneratedRegex(@"RN\s*-\s*\d+\s*-\s*[\d.]+\s*_\s*v\d+\s*\|\s*\d+\s*Release \d+ Driver for Windows, Version [\d.]+")]
    private static partial Regex Footer();

    [GeneratedRegex(@"\s\d+\.\d+(\.\d+)?\s*$")]
    private static partial Regex NextSectionNumber();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
