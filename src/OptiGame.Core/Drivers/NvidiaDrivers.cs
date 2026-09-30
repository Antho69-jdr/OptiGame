using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace OptiGame.Core.Drivers;

/// <summary>Dernier pilote NVIDIA publié pour une carte (service de recherche de pilotes de nvidia.com).</summary>
public sealed record NvidiaDriver(string Name, string Version, DateOnly? ReleaseDate, Uri DownloadUrl, string? SizeText, Uri? DetailsUrl,
    IReadOnlyList<string> SupportedSeries);

/// <summary>
/// Pilotes NVIDIA. Services non documentés : formats vérifiés sur de vraies réponses le 2026-09-30 (copies dans les
/// tests). Seul le modèle de la carte graphique est envoyé à NVIDIA.
/// <list type="bullet">
/// <item><c>www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3</c> : XML, un <c>LookupValue</c> par produit
/// (<c>Name</c>, <c>Value</c> = pfid, attribut <c>ParentID</c> = psid de la série).</item>
/// <item><c>gfwsl.geforce.com/…/AjaxDriverService.php?func=DriverManualLookup</c> : JSON (annoncé text/html), IDS[0].downloadInfo
/// avec Version (« 617.14 »), ReleaseDateTime (« Tue Sep 22, 2026 » en anglais), DownloadURL, DownloadURLFileSize,
/// DetailsURL, Name (encodé URL), series[].seriesname.</item>
/// </list>
/// </summary>
public static class NvidiaDrivers
{
    /// <summary>Windows 11 64 bits (réponse vérifiée : OSName « Windows 11 »).</summary>
    public const int Windows11OsId = 57;

    /// <summary>Anglais : date au format fixe « Tue Sep 22, 2026 ».</summary>
    public const int EnglishLanguageCode = 1033;

    public static readonly Uri ProductListUri = new("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3");

    /// <summary>Hôtes autorisés pour le téléchargement : jamais d'installeur venant d'ailleurs.</summary>
    public static bool IsOfficialDownload(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.Host.EndsWith(".download.nvidia.com", StringComparison.OrdinalIgnoreCase);

    public static Uri LookupUri(int seriesId, int productId) => new(
        "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup" +
        $"&psid={seriesId}&pfid={productId}&osID={Windows11OsId}&languageCode={EnglishLanguageCode}&isWHQL=1&dch=1&sort1=0&numberOfResults=1");

    /// <summary>
    /// Version Windows du pilote (« 32.0.15.9186 ») → version NVIDIA (« 591.86 ») : les 5 derniers chiffres des deux
    /// derniers nombres (15 + 9186 → 59186). Null si la version n'a pas cette forme.
    /// </summary>
    public static string? FromWindowsVersion(string? windowsVersion)
    {
        var parts = windowsVersion?.Split('.');
        if (parts is not { Length: 4 } || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)) || parts[3].Length > 4) return null;
        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5) return null;
        var last = digits[^5..];
        return $"{int.Parse(last[..3], CultureInfo.InvariantCulture)}.{last[3..]}";
    }

    /// <summary>Compare deux versions NVIDIA (« 617.14 » &gt; « 591.86 ») ; null si l'une est illisible.</summary>
    public static int? Compare(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) ? va.CompareTo(vb) : null;

    /// <summary>
    /// Produit NVIDIA d'une carte : nom exact, sans le préfixe « NVIDIA » (« GeForce RTX 3070 » ≠ « GeForce RTX 3070 Ti »
    /// ≠ « GeForce RTX 3070 Laptop GPU »). Null si la carte n'est pas dans la liste.
    /// </summary>
    public static (int SeriesId, int ProductId)? FindProduct(string gpuName, string productListXml)
    {
        var name = gpuName.Trim();
        if (name.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase)) name = name["NVIDIA ".Length..].Trim();

        foreach (var value in XDocument.Parse(productListXml).Descendants("LookupValue"))
        {
            if (!string.Equals(value.Element("Name")?.Value.Trim(), name, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(value.Attribute("ParentID")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var series) &&
                int.TryParse(value.Element("Value")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var product))
            {
                return (series, product);
            }
        }
        return null;
    }

    /// <summary>Réponse du service de recherche → pilote ; null si la réponse ne contient pas de pilote exploitable.</summary>
    public static NvidiaDriver? ParseLookup(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Text(root, "Success") != "1" || !root.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0 ||
            !ids[0].TryGetProperty("downloadInfo", out var info))
        {
            return null;
        }

        var version = Text(info, "Version");
        if (version is null || !Uri.TryCreate(Text(info, "DownloadURL"), UriKind.Absolute, out var download)) return null;

        DateOnly? released = DateOnly.TryParseExact(Text(info, "ReleaseDateTime"), "ddd MMM d, yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
        Uri? details = Uri.TryCreate(Text(info, "DetailsURL"), UriKind.Absolute, out var d) ? d : null;
        var series = info.TryGetProperty("series", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(s => Unescape(Text(s, "seriesname"))).OfType<string>().ToList()
            : [];

        return new NvidiaDriver(Unescape(Text(info, "Name")) ?? "Pilote NVIDIA", version, released, download,
            Text(info, "DownloadURLFileSize"), details, series);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static string? Unescape(string? text) => text is null ? null : Uri.UnescapeDataString(text);
}
