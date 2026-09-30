using System.Globalization;
using System.Text.Json;

namespace OptiGame.Core.Artwork;

/// <summary>Jeu trouvé sur IGDB, avec les identifiants d'images utiles (jaquette et bannière).</summary>
public sealed record IgdbGame(long Id, string Name, int? Year, string? CoverImageId, string? HeroImageId);

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
