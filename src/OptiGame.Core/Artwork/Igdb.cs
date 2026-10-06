using System.Globalization;
using System.Text.Json;
using OptiGame.Core.Library;

namespace OptiGame.Core.Artwork;

/// <summary>Jeu trouvé sur IGDB, avec les identifiants d'images utiles (jaquette et bannière).</summary>
public sealed record IgdbGame(long Id, string Name, int? Year, string? CoverImageId, string? HeroImageId);

public enum BackgroundKind
{
    /// <summary>Illustration (« artwork ») : image promotionnelle, souvent sans interface.</summary>
    Artwork,

    /// <summary>Capture d'écran du jeu.</summary>
    Screenshot,
}

/// <summary>Image IGDB proposée comme fond de la fiche du jeu.</summary>
public sealed record IgdbBackground(string ImageId, BackgroundKind Kind);

/// <summary>
/// Requêtes et réponses de l'API IGDB v4 (documentation api-docs.igdb.com consultée le 2026-09-30) :
/// POST https://api.igdb.com/v4/games, en-têtes Client-ID et Authorization: Bearer, corps en syntaxe « Apicalypse ».
/// Images : https://images.igdb.com/igdb/image/upload/t_{taille}/{image_id}.jpg.
/// </summary>
public static class Igdb
{
    public const string GamesEndpoint = "https://api.igdb.com/v4/games";
    public const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";

    /// <summary>Jaquette 528×748 (cover_big en haute définition).</summary>
    public const string CoverSize = "cover_big_2x";

    /// <summary>Bannière 1920×1080.</summary>
    public const string HeroSize = "1080p";

    public static string SearchQuery(string name, int limit = 12) =>
        $"search \"{Escape(name)}\"; fields name,first_release_date,cover.image_id,artworks.image_id,screenshots.image_id; limit {limit};";

    public static string ImageUrl(string imageId, string size) =>
        $"https://images.igdb.com/igdb/image/upload/t_{size}/{Uri.EscapeDataString(imageId)}.jpg";

    /// <summary>Réponse de /games → jeux. La bannière est la première illustration, à défaut la première capture d'écran.</summary>
    public static IReadOnlyList<IgdbGame> ParseGames(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Réponse IGDB inattendue : un tableau de jeux était attendu.");
        }

        var games = new List<IgdbGame>();
        foreach (var game in document.RootElement.EnumerateArray())
        {
            if (!game.TryGetProperty("id", out var id) || !game.TryGetProperty("name", out var name)) continue;

            int? year = game.TryGetProperty("first_release_date", out var date) && date.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).Year
                : null;

            var cover = game.TryGetProperty("cover", out var c) ? ImageId(c) : null;
            var hero = FirstImageId(game, "artworks") ?? FirstImageId(game, "screenshots");
            games.Add(new IgdbGame(id.GetInt64(), name.GetString() ?? "", year, cover, hero));
        }
        return games;
    }

    /// <summary>
    /// Meilleur résultat pour un nom de profil : nom identique (hors ponctuation et casse), sinon le premier résultat
    /// (IGDB classe par pertinence). Null si aucun résultat.
    /// </summary>
    public static IgdbGame? BestMatch(string name, IReadOnlyList<IgdbGame> results)
    {
        var wanted = Normalize(name);
        return results.FirstOrDefault(g => Normalize(g.Name) == wanted && g.CoverImageId is not null)
            ?? results.FirstOrDefault(g => g.CoverImageId is not null);
    }


    /// <summary>Aperçu d'un fond dans « Changer le fond… » (569×320).</summary>
    public const string BackgroundThumbSize = "screenshot_med";

    /// <summary>Toutes les illustrations et captures d'écran d'un jeu IGDB (fonds proposés), par son identifiant.</summary>
    public static string BackgroundsQuery(long gameId) =>
        $"fields artworks.image_id,screenshots.image_id; where id = {gameId.ToString(CultureInfo.InvariantCulture)}; limit 1;";

    /// <summary>Réponse de BackgroundsQuery → images, illustrations d'abord (faites pour servir de fond), sans doublon.</summary>
    public static IReadOnlyList<IgdbBackground> ParseBackgrounds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Réponse IGDB inattendue : un tableau de jeux était attendu.");
        }
        var backgrounds = new List<IgdbBackground>();
        foreach (var game in document.RootElement.EnumerateArray())
        {
            foreach (var (property, kind) in new[] { ("artworks", BackgroundKind.Artwork), ("screenshots", BackgroundKind.Screenshot) })
            {
                if (!game.TryGetProperty(property, out var images) || images.ValueKind != JsonValueKind.Array) continue;
                foreach (var image in images.EnumerateArray())
                {
                    if (ImageId(image) is { } id && backgrounds.All(b => b.ImageId != id)) backgrounds.Add(new IgdbBackground(id, kind));
                }
            }
        }
        return backgrounds;
    }

    /// <summary>Requêtes multiples (/v4/multiquery) : jusqu'à 10 requêtes par appel (documentation IGDB).</summary>
    public const string MultiQueryEndpoint = "https://api.igdb.com/v4/multiquery";

    public const int MaxQueriesPerMultiQuery = 10;

    // total_rating_count : parmi plusieurs jeux du même nom (« Hades »), le plus connu est le bon.
    private const string TaxonomyFields = "name,total_rating_count,genres.name,themes.name,game_modes.name,player_perspectives.name";

    /// <summary>Nom tel qu'IGDB l'écrit : sans ™ ® © (« The Sims™ 3 » → « The Sims 3 »).</summary>
    public static string CleanName(string name) =>
        string.Join(' ', name.Replace("™", " ").Replace("®", " ").Replace("©", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Genres, thèmes, modes et points de vue de jusqu'à 10 jeux, par NOM EXACT sans tenir compte de la casse (requête « i » =
    /// names[i]). Vérifié le 2026-10-06 : « search » ne renvoie RIEN dans une requête multiple (« [] »), « where name ~ » oui.
    /// </summary>
    public static string TaxonomyMultiQuery(IReadOnlyList<string> names)
    {
        if (names.Count is 0 or > MaxQueriesPerMultiQuery) throw new ArgumentException($"1 à {MaxQueriesPerMultiQuery} noms par requête.", nameof(names));
        return string.Concat(names.Select((name, i) =>
            $"query games \"{i.ToString(CultureInfo.InvariantCulture)}\" {{ fields {TaxonomyFields}; where name ~ \"{Escape(CleanName(name))}\"; limit 5; }};\n"));
    }

    /// <summary>Recherche d'UN jeu par son nom (/games), pour ceux que le nom exact n'a pas trouvés (édition, sous-titre…).</summary>
    public static string TaxonomySearchQuery(string name) => $"search \"{Escape(CleanName(name))}\"; fields {TaxonomyFields}; limit 5;";

    /// <summary>Réponse de TaxonomyMultiQuery → genres et types de chaque nom demandé, ou null (voir <see cref="BestTaxonomyMatch"/>).</summary>
    public static IReadOnlyList<GameTags?> ParseTaxonomyMultiQuery(string json, IReadOnlyList<string> names)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Réponse IGDB inattendue : un tableau de résultats était attendu.");
        }
        var tags = new GameTags?[names.Count];
        foreach (var query in document.RootElement.EnumerateArray())
        {
            if (query.TryGetProperty("name", out var label) && int.TryParse(label.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < names.Count && query.TryGetProperty("result", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                tags[index] = BestTaxonomyMatch(results, names[index]);
            }
        }
        return tags;
    }

    /// <summary>Réponse de TaxonomySearchQuery (tableau de jeux) → genres et types, ou null.</summary>
    public static GameTags? ParseTaxonomySearch(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Réponse IGDB inattendue : un tableau de jeux était attendu.");
        }
        return BestTaxonomyMatch(document.RootElement, name);
    }

    /// <summary>
    /// Jeu retenu parmi les résultats : même nom hors ponctuation et casse, sinon un nom qui commence par lui (« Control » →
    /// « Control Ultimate Edition »). Sinon null : un autre jeu donnerait de faux genres.
    /// </summary>
    private static GameTags? BestTaxonomyMatch(JsonElement results, string name)
    {
        var wanted = Normalize(CleanName(name));
        var games = results.EnumerateArray()
            .Where(g => g.ValueKind == JsonValueKind.Object && g.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            .Select(g => (Game: g, Key: Normalize(g.GetProperty("name").GetString()!), Votes: g.TryGetProperty("total_rating_count", out var v) && v.TryGetInt32(out var c) ? c : 0))
            .OrderByDescending(g => g.Votes)
            .ToList();
        var match = games.Where(g => g.Key == wanted).Select(g => (JsonElement?)g.Game).FirstOrDefault()
                    ?? games.Where(g => wanted.Length >= 4 && g.Key.StartsWith(wanted, StringComparison.Ordinal)).Select(g => (JsonElement?)g.Game).FirstOrDefault();
        return match is { } game ? Tags(game) : null;
    }

    private static GameTags Tags(JsonElement game)
    {
        IEnumerable<string> Names(string property) =>
            game.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out var n) ? n.GetString() : null).OfType<string>().ToList()
                : [];
        return GameTaxonomy.FromIgdb(Names("genres"), Names("themes"), Names("game_modes"), Names("player_perspectives"));
    }

    public static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(c => char.ToLower(c, CultureInfo.InvariantCulture)).ToArray());

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string? ImageId(JsonElement image) =>
        image.ValueKind == JsonValueKind.Object && image.TryGetProperty("image_id", out var id) ? id.GetString() : null;

    private static string? FirstImageId(JsonElement game, string property) =>
        game.TryGetProperty(property, out var images) && images.ValueKind == JsonValueKind.Array
            ? images.EnumerateArray().Select(ImageId).FirstOrDefault(i => i is not null)
            : null;
}
