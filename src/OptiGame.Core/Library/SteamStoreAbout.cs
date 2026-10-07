using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Library;

/// <summary>Paragraphe de la description d'un jeu (texte seul ; un titre de section est mis en valeur).</summary>
public sealed record GameAboutBlock(string Text, bool IsHeading);

/// <summary>
/// Avis des joueurs sur Steam, toutes langues. <paramref name="Score"/> = review_score de Steam (1 à 9) ; 0 = trop peu d'avis
/// pour un qualificatif (Steam affiche alors « 3 évaluations »).
/// </summary>
public sealed record PlayerReviews(int Score, string Label, int Positive, int Total)
{
    public bool IsRated => Score > 0 && Total > 0;

    /// <summary>Part des avis positifs, arrondie à l'unité la plus proche.</summary>
    public int Percent => Total > 0 ? (int)Math.Round(Positive * 100.0 / Total, MidpointRounding.AwayFromZero) : 0;
}

/// <summary>Note de la presse publiée par Steam (Metacritic), sur 100, et sa page.</summary>
public sealed record PressScore(int Score, string? Url);

/// <summary>
/// Bande-annonce publiée sur le magasin Steam : flux HLS en H.264 (décodage matériel le plus répandu) et vignette, toutes deux
/// en https sur *.steamstatic.com.
/// </summary>
public sealed record GameTrailer(string Name, string ThumbnailUrl, string HlsUrl);

/// <summary>Présentation d'un jeu sur sa fiche : description, avis des joueurs, note de la presse, bandes-annonces.</summary>
public sealed record GameAbout(string Summary, IReadOnlyList<GameAboutBlock> Details, PressScore? Press, PlayerReviews? Players,
    IReadOnlyList<GameTrailer> Trailers)
{
    public bool IsEmpty => Summary.Length == 0 && Details.Count == 0 && Press is null && Players is null && Trailers.Count == 0;
}

/// <summary>Ce que appdetails dit d'un jeu (sans les avis, qui viennent d'appreviews).</summary>
public sealed record SteamDetails(string Summary, IReadOnlyList<GameAboutBlock> Details, PressScore? Press, IReadOnlyList<GameTrailer> Trailers);

/// <summary>
/// Description et avis d'un jeu sur le magasin Steam, en français. Formats relevés le 2026-10-07 (échantillons réels dans les
/// tests) :
/// <list type="bullet">
/// <item>appdetails avec filters=basic,metacritic,movies et l=french (19 Ko pour Portal 2 et ses 18 vidéos ; metacritic et
/// movies ne sont PAS dans basic) : short_description (texte), about_the_game (HTML : h2, p, br, ul/li, img, video),
/// metacritic { score, url } (absent pour ARC Raiders), movies [{ name, thumbnail, hls_h264, dash_h264, dash_av1, highlight }]
/// (plus aucun fichier MP4 pour les vidéos récentes ; vignette 293×165 pour les anciennes, 600×337 pour les récentes) ;</item>
/// <item>appreviews json=1, language=all, num_per_page=0, l=french : query_summary { review_score 0-9, review_score_desc
/// (« très positives », « positives », « moyennes », « 3 évaluations », « aucune évaluation »), total_positive,
/// total_reviews }.</item>
/// </list>
/// Services non documentés par Valve : seul l'appid est envoyé.
/// </summary>
public static partial class SteamStoreAbout
{
    public static Uri DetailsUri(string appId) =>
        new($"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(appId)}&l=french&filters=basic,metacritic,movies");

    /// <summary>Bandes-annonces gardées au plus (les premières : celle mise en avant par Steam, puis l'ordre du magasin).</summary>
    public const int MaxTrailers = 4;

    public static Uri ReviewsUri(string appId) =>
        new($"https://store.steampowered.com/appreviews/{Uri.EscapeDataString(appId)}?json=1&language=all&purchase_type=all&num_per_page=0&l=french");

    /// <summary>Description, note de la presse et bandes-annonces ; null si Steam ne connaît pas l'appid (success = false). JsonException si illisible.</summary>
    public static SteamDetails? ParseDetails(string json, string appId)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(appId, out var app)
            || !app.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
            || !app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var summary = data.TryGetProperty("short_description", out var shortText) && shortText.ValueKind == JsonValueKind.String
            ? Collapse(WebUtility.HtmlDecode(StripTags(shortText.GetString()!)))
            : "";
        var details = data.TryGetProperty("about_the_game", out var about) && about.ValueKind == JsonValueKind.String
            ? HtmlToBlocks(about.GetString()!)
            : [];
        PressScore? press = null;
        if (data.TryGetProperty("metacritic", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("score", out var score) && score.TryGetInt32(out var value) && value is > 0 and <= 100)
        {
            var url = meta.TryGetProperty("url", out var link) && link.ValueKind == JsonValueKind.String
                && Uri.TryCreate(link.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? uri.ToString()
                : null;
            press = new PressScore(value, url);
        }
        return new SteamDetails(summary, details, press, ParseTrailers(data));
    }

    /// <summary>Bandes-annonces lisibles (flux HLS H.264 et vignette sur *.steamstatic.com), celle mise en avant d'abord.</summary>
    private static IReadOnlyList<GameTrailer> ParseTrailers(JsonElement data)
    {
        if (!data.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array) return [];
        var trailers = new List<(GameTrailer Trailer, bool Highlight)>();
        foreach (var movie in movies.EnumerateArray())
        {
            if (movie.ValueKind != JsonValueKind.Object) continue;
            var hls = SteamUrl(movie, "hls_h264");
            var thumbnail = SteamUrl(movie, "thumbnail");
            if (hls is null || thumbnail is null) continue;
            var name = movie.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? Collapse(WebUtility.HtmlDecode(n.GetString()!)) : "";
            var highlight = movie.TryGetProperty("highlight", out var h) && h.ValueKind == JsonValueKind.True;
            trailers.Add((new GameTrailer(name, thumbnail, hls), highlight));
        }
        return trailers.OrderByDescending(t => t.Highlight).Select(t => t.Trailer).Take(MaxTrailers).ToList(); // tri stable
    }

    /// <summary>Adresse https d'un serveur de Steam (*.steamstatic.com) ; null sinon.</summary>
    private static string? SteamUrl(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && IsSteamMedia(uri)
            ? uri.AbsoluteUri
            : null;

    /// <summary>Médias du magasin Steam : https, hôte *.steamstatic.com.</summary>
    public static bool IsSteamMedia(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Avis des joueurs ; null si aucun avis ou réponse en échec. JsonException si illisible.</summary>
    public static PlayerReviews? ParseReviews(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("success", out var success) || !success.TryGetInt32(out var ok) || ok != 1
            || !document.RootElement.TryGetProperty("query_summary", out var summary))
        {
            return null;
        }
        var total = Int(summary, "total_reviews");
        if (total <= 0) return null;
        var label = summary.TryGetProperty("review_score_desc", out var desc) && desc.ValueKind == JsonValueKind.String ? desc.GetString()! : "";
        return new PlayerReviews(Int(summary, "review_score"), label, Int(summary, "total_positive"), total);
    }

    /// <summary>
    /// Paragraphes d'une description HTML de Steam : titres (h1-h6) mis à part, listes en « • », images et vidéos retirées,
    /// entités décodées. Aucun HTML n'est jamais affiché.
    /// </summary>
    public static IReadOnlyList<GameAboutBlock> HtmlToBlocks(string html)
    {
        var text = MediaRegex().Replace(html, " ");
        text = ListItemRegex().Replace(text, "\n• ");
        text = HeadingRegex().Replace(text, m => $"\n\u0001{m.Groups[1].Value}\n");
        text = BreakRegex().Replace(text, "\n");
        text = StripTags(text);

        var blocks = new List<GameAboutBlock>();
        foreach (var raw in text.Split('\n'))
        {
            var isHeading = raw.Contains('\u0001');
            var line = Collapse(WebUtility.HtmlDecode(raw.Replace("\u0001", "")));
            if (line.Length == 0 || line == "•") continue;
            blocks.Add(new GameAboutBlock(line, isHeading));
        }
        return blocks;
    }

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string StripTags(string html) => TagRegex().Replace(html, "");

    /// <summary>Espaces multiples (y compris insécables du HTML) ramenés à un seul, bords retirés.</summary>
    private static string Collapse(string text) => SpacesRegex().Replace(text.Replace(' ', ' '), " ").Trim();

    [GeneratedRegex(@"<(video|audio|iframe|script|style)\b[^>]*>.*?</\1\s*>|<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MediaRegex();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<h[1-6]\b[^>]*>(.*?)</h[1-6]\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"<br\s*/?>|</p\s*>|<p\b[^>]*>|</?(ul|ol|div)\b[^>]*>|</li\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t\r]+")]
    private static partial Regex SpacesRegex();
}
