using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Library;

/// <summary>
/// Présentation d'un jeu sur le magasin GOG (services publics non documentés, relevés le 2026-10-08, échantillons réels dans les
/// tests) :
/// <list type="bullet">
/// <item><c>api.gog.com/products/&lt;id&gt;?expand=description,videos&amp;locale=fr-FR</c> (déjà contacté pour « Voir sur GOG ») :
/// description { lead, full, whats_cool_about_it } en HTML (img, br, p, a ; demandée en français, mais certains éditeurs ne
/// l'écrivent qu'en anglais : Fiendish Freddy's) ; videos [{ video_url, provider }] = YouTube (« embed ») ou le lecteur
/// fast.wistia.net ; produit inconnu = HTTP 404 ;</item>
/// <item><c>reviews.gog.com/v1/products/&lt;id&gt;/averageRating?reviewer=verified_owner</c> : { value (sur 5), count } ; aucun
/// avis = { 0.0, 0 } (aussi pour un produit inconnu).</item>
/// </list>
/// Seul l'identifiant du produit est envoyé.
/// </summary>
public static partial class GogStoreAbout
{
    public static Uri ProductUri(string productId) =>
        new($"https://api.gog.com/products/{Uri.EscapeDataString(productId)}?expand=description,videos&locale=fr-FR");

    public static Uri RatingUri(string productId) =>
        new($"https://reviews.gog.com/v1/products/{Uri.EscapeDataString(productId)}/averageRating?reviewer=verified_owner");

    /// <summary>Description (résumé = premier paragraphe) et bandes-annonces. JsonException si illisible.</summary>
    public static (string Summary, IReadOnlyList<GameAboutBlock> Details, IReadOnlyList<WebVideo> Videos) ParseProduct(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        IReadOnlyList<GameAboutBlock> details = [];
        if (root.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.Object
            && description.TryGetProperty("full", out var full) && full.ValueKind == JsonValueKind.String)
        {
            // Encarts publicitaires de GOG (« Click here to discover the entire … collection! ») : pas la description du jeu.
            details = SteamStoreAbout.HtmlToBlocks(PromoModule().Replace(full.GetString()!, " "));
        }
        var videos = new List<WebVideo>();
        if (root.TryGetProperty("videos", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var video in list.EnumerateArray())
            {
                var url = video.ValueKind == JsonValueKind.Object && video.TryGetProperty("video_url", out var u) && u.ValueKind == JsonValueKind.String
                    ? WebVideo.Normalize(u.GetString())
                    : null;
                if (url is not null && videos.All(v => v.Url != url)) videos.Add(new WebVideo($"Vidéo {videos.Count + 1}", url));
                if (videos.Count == SteamStoreAbout.MaxTrailers) break;
            }
        }
        return (GameAbout.SummaryFrom(details), details, videos);
    }

    /// <summary>Note moyenne des acheteurs (sur 5) ; null sans avis. JsonException si illisible.</summary>
    public static CommunityRating? ParseRating(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("count", out var count) || !count.TryGetInt32(out var votes) || votes <= 0) return null;
        if (!root.TryGetProperty("value", out var value) || !value.TryGetDouble(out var average) || average is <= 0 or > 5) return null;
        return new CommunityRating(Math.Round(average, 1), 5, votes);
    }

    [GeneratedRegex("""<p\b[^>]*class="module"[^>]*>.*?</p\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PromoModule();

    /// <summary>« 4,4 » : une décimale, virgule française.</summary>
    public static string Format(double value) => value.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"));
}
