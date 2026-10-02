using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Drivers;

/// <summary>Plateforme AMD d'une carte mère : socket (am4, am5) et chipset (b450, x670e…), noms des pages d'AMD.</summary>
public sealed record AmdBoard(string Socket, string Chipset);

/// <summary>Dernier « AMD Chipset Software » publié pour une plateforme.</summary>
public sealed record AmdChipsetRelease(string Version, string? SizeText, DateOnly? ReleaseDate, Uri DownloadUrl, Uri? ReleaseNotes);

/// <summary>Pilote AMD installé (périphérique de la carte mère : PSP, GPIO, SMBus, PCI…).</summary>
public sealed record AmdDriverInfo(string DeviceName, string? Version, DateOnly? Date);

/// <summary>État du logiciel de chipset AMD.</summary>
public sealed record ChipsetDriverStatus(string Description, string InstalledText, AmdChipsetRelease? Latest, DriverState State, string Message, Uri? SupportPage);

/// <summary>
/// Logiciel de chipset AMD (« AMD Chipset Software » : pilotes PSP, GPIO, SMBus, PCI… et réglages d'énergie des Ryzen).
/// AMD ne publie pas de service de recherche : la page de téléchargement du chipset est lue (lecture seule). Format vérifié
/// le 2026-10-02 sur https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/b450.html (extrait dans les
/// tests) : un bloc <c>&lt;article class="… driver-download-details"&gt;</c> par pilote, avec <c>&lt;h4&gt;</c> (titre),
/// « Revision Number », « File Size », « Release Date » (AAAA-MM-JJ), le lien drivers.amd.com et les notes de version.
/// Sections Windows 11, puis Windows 10, puis Windows 7 (lien « AMD_Chipset_Software_Win7_… », écarté).
/// </summary>
public static partial class AmdChipset
{
    private static readonly HashSet<string> Am4 = ["a320", "b350", "x370", "b450", "x470", "a520", "b550", "x570"];
    private static readonly HashSet<string> Am5 = ["a620", "b650", "b650e", "x670", "x670e", "b840", "b850", "x870", "x870e"];

    /// <summary>Chipset d'après le modèle de carte mère (« B450M MORTAR MAX » → am4/b450) ; null si non reconnu.</summary>
    public static AmdBoard? BoardFromProduct(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return null;
        foreach (Match match in ChipsetToken().Matches(product.ToUpperInvariant()))
        {
            var chipset = (match.Groups[1].Value + match.Groups[2].Value).ToLowerInvariant();
            if (Am4.Contains(chipset)) return new AmdBoard("am4", chipset);
            if (Am5.Contains(chipset)) return new AmdBoard("am5", chipset);
        }
        return null;
    }

    /// <summary>Page de téléchargement d'AMD pour ce chipset (forme vérifiée pour am4/b450).</summary>
    public static Uri SupportPage(AmdBoard board) =>
        new($"https://www.amd.com/en/support/downloads/drivers.html/chipsets/{board.Socket}/{board.Chipset}.html");

    /// <summary>Page générale des pilotes d'AMD (repli quand le chipset n'est pas reconnu).</summary>
    public static readonly Uri GenericSupportPage = new("https://www.amd.com/en/support/download/drivers.html");

    /// <summary>Premier « AMD Chipset Drivers » de la page (Windows 11 vient en premier) ; null si la page n'en contient pas.</summary>
    public static AmdChipsetRelease? ParsePage(string html)
    {
        foreach (Match article in Article().Matches(html))
        {
            var block = article.Value;
            if (!Text(Title(), block).Equals("AMD Chipset Drivers", StringComparison.OrdinalIgnoreCase)) continue;

            var link = ChipsetLink().Match(block);
            if (!link.Success || !Uri.TryCreate(link.Value, UriKind.Absolute, out var download)) continue; // Windows 7 : autre nom
            var version = Field(block, "Revision Number");
            if (version is null || !version.Equals(link.Groups[1].Value, StringComparison.Ordinal)) continue;

            DateOnly? date = DateOnly.TryParseExact(Field(block, "Release Date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            Uri? notes = ReleaseNotes().Match(block) is { Success: true } n && Uri.TryCreate(new Uri("https://www.amd.com/"), WebUtility.HtmlDecode(n.Groups[1].Value), out var u) ? u : null;
            return new AmdChipsetRelease(version, Field(block, "File Size"), date, download, notes);
        }
        return null;
    }

    /// <summary>
    /// État : le paquet installé est comparé à la dernière version. Sans paquet (pilotes installés séparément, comme sur la
    /// machine de dev), on compare les dates : la version publiée est plus récente que le pilote AMD le plus récent.
    /// </summary>
    public static ChipsetDriverStatus Evaluate(string description, string? packageVersion, IReadOnlyList<AmdDriverInfo> drivers,
        AmdChipsetRelease? latest, Uri supportPage, string? lookupError = null)
    {
        var newest = drivers.Where(d => d.Date is not null).OrderByDescending(d => d.Date).FirstOrDefault();
        var oldest = drivers.Where(d => d.Date is not null).OrderBy(d => d.Date).FirstOrDefault();
        var installed = packageVersion is not null
            ? $"AMD Chipset Software {packageVersion} installé."
            : drivers.Count == 0
                ? "Aucun pilote de chipset AMD installé."
                : $"AMD Chipset Software non installé ; {drivers.Count} pilote(s) AMD séparé(s)" +
                  (oldest is not null ? $", le plus ancien ({oldest.DeviceName}) du {oldest.Date:dd/MM/yyyy}" : "") + ".";

        if (latest is null)
        {
            return new(description, installed, null, DriverState.Unknown, lookupError ?? "Dernière version introuvable sur le site d'AMD.", supportPage);
        }

        bool newer;
        if (packageVersion is not null && Version.TryParse(packageVersion, out var have) && Version.TryParse(latest.Version, out var available))
        {
            newer = available > have;
        }
        else if (packageVersion is null && newest?.Date is { } newestDate && latest.ReleaseDate is { } released)
        {
            newer = released > newestDate;
        }
        else
        {
            newer = packageVersion is null; // ni dates ni version comparables : le paquet manque, il est proposé
        }

        return newer
            ? new(description, installed, latest, DriverState.UpdateAvailable, $"Nouveau logiciel de chipset disponible : {latest.Version}.", supportPage)
            : new(description, installed, latest, DriverState.UpToDate, $"Logiciel de chipset à jour ({latest.Version}).", supportPage);
    }

    private static string? Field(string block, string label) =>
        Regex.Match(block, $@"<strong>\s*{Regex.Escape(label)}\s*</strong>\s*<p>\s*(.*?)\s*</p>", RegexOptions.Singleline) is { Success: true } m
            ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim()
            : null;

    private static string Text(Regex regex, string block) =>
        regex.Match(block) is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";

    // B450 / X670E / B650 (le « M » de « B650M » = micro-ATX, pas le « E »).
    [GeneratedRegex(@"(?<![A-Z0-9])([ABX](?:320|350|370|450|470|520|550|570|620|650|670|840|850|870))(E(?![A-Z]))?")]
    private static partial Regex ChipsetToken();

    [GeneratedRegex(@"<article\s+class=""[^""]*driver-download-details[^""]*"".*?</article>", RegexOptions.Singleline)]
    private static partial Regex Article();

    [GeneratedRegex(@"<h4>\s*(.*?)\s*</h4>", RegexOptions.Singleline)]
    private static partial Regex Title();

    [GeneratedRegex(@"https://drivers\.amd\.com/drivers/AMD_Chipset_Software_(\d+(?:\.\d+)+)\.exe")]
    private static partial Regex ChipsetLink();

    [GeneratedRegex(@"href=""([^""]*release-notes[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseNotes();
}
