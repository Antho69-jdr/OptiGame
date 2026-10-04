using System.Text;
using System.Text.Json;
using OptiGame.Core.Profiles;

namespace OptiGame.Core.Library;

/// <summary>Jeu possédé dans un magasin autre que Steam (section « non installés » de « Mes jeux »).</summary>
/// <param name="Key">Identifiant dans le magasin : « namespace:item:app » (Epic) ou releaseKey de GOG Galaxy (« gog_… »).</param>
/// <param name="Genres">Genres ramenés aux noms français de <see cref="SteamTaxonomy"/>, pour un filtre unique.</param>
public sealed record StoreOwnedGame(GameSource Store, string Key, string Name, IReadOnlyList<string> Genres, string? CoverUrl);

/// <summary>Ligne de la base de GOG Galaxy : releaseKey, propriétés et pièces JSON (titre, images, méta).</summary>
public sealed record GalaxyRow(string ReleaseKey, bool IsDlc, bool IsVisible, string? TitleJson, string? ImagesJson, string? MetaJson);

/// <summary>
/// Jeux possédés des autres magasins, vérifiés sur la machine de dev le 2026-10-04 :
/// <list type="bullet">
/// <item>Epic : <c>Data\Catalog\catcache.bin</c> = JSON en base64 de 1866 éléments (dont assets Unreal) ; jeu = catégorie « games »
/// et <c>mainGameItem.id</c> vide (sinon DLC) → 348 jeux, jaquette <c>DieselGameBoxTall</c> (1200×1600, réduite par le CDN avec
/// <c>?h=528&amp;w=396&amp;resize=1</c>). Choix de l'utilisateur : seule source pour Epic (la liste Epic de GOG Galaxy est périmée).</item>
/// <item>GOG Galaxy (galaxy-2.0.db) : bibliothèque de toutes les plateformes liées ; on ne garde que les jeux GOG (gog_, 13).
/// Visibles et hors DLC. Jaquette <c>verticalCover</c> en .webp, servie aussi en .jpg (vérifié : 200 image/jpeg, 342×482).</item>
/// </list>
/// </summary>
public static class StoreCatalogs
{
    public const string EpicCoverSize = "?h=528&w=396&resize=1";

    public static IReadOnlyList<StoreOwnedGame> ParseEpicCatalog(byte[] catcache)
    {
        var json = Convert.FromBase64String(Encoding.ASCII.GetString(catcache).Trim());
        using var document = JsonDocument.Parse(json);
        var games = new List<StoreOwnedGame>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var categories = item.TryGetProperty("categories", out var c)
                ? c.EnumerateArray().Select(x => x.TryGetProperty("path", out var p) ? p.GetString() : null).ToList()
                : [];
            var mainGame = item.TryGetProperty("mainGameItem", out var m) && m.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (!categories.Contains("games") || !string.IsNullOrEmpty(mainGame)) continue;
            var appName = item.TryGetProperty("releaseInfo", out var r) && r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray().Select(x => x.TryGetProperty("appId", out var a) ? a.GetString() : null).FirstOrDefault(a => !string.IsNullOrEmpty(a))
                : null;
            if (appName is null || Text(item, "title") is not { } title || Text(item, "namespace") is not { } ns || Text(item, "id") is not { } itemId) continue;
            var cover = item.TryGetProperty("keyImages", out var images)
                ? images.EnumerateArray().Where(i => Text(i, "type") == "DieselGameBoxTall").Select(i => Text(i, "url")).FirstOrDefault()
                : null;
            games.Add(new StoreOwnedGame(GameSource.Epic, $"{ns}:{itemId}:{appName}", title, [], cover is null ? null : cover + EpicCoverSize));
        }
        return games;
    }

    public static IReadOnlyList<StoreOwnedGame> FromGalaxy(IEnumerable<GalaxyRow> rows)
    {
        var games = new List<StoreOwnedGame>();
        foreach (var row in rows.Where(r => r.IsVisible && !r.IsDlc))
        {
            if (!row.ReleaseKey.StartsWith("gog_", StringComparison.Ordinal) || JsonText(row.TitleJson, "title") is not { Length: > 0 } title) continue;
            var cover = JsonText(row.ImagesJson, "verticalCover")?.Replace(".webp", ".jpg", StringComparison.OrdinalIgnoreCase);
            games.Add(new StoreOwnedGame(GameSource.Gog, row.ReleaseKey, title, GalaxyGenres(row.MetaJson), cover));
        }
        return games.DistinctBy(g => (g.Store, NameKey(g.Name))).ToList();
    }

    /// <summary>Genres de GOG Galaxy (anglais, plus fins) regroupés dans les genres du magasin Steam, pour un filtre commun.</summary>
    private static readonly Dictionary<string, string[]> GalaxyGenreMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Adventure"] = ["Aventure"], ["Point-and-click"] = ["Aventure"], ["Visual Novel"] = ["Aventure"],
        ["Indie"] = ["Indépendant"], ["Role-playing (RPG)"] = ["RPG"], ["Simulator"] = ["Simulation"],
        ["Strategy"] = ["Stratégie"], ["Turn-based strategy (TBS)"] = ["Stratégie"], ["Real Time Strategy (RTS)"] = ["Stratégie"],
        ["Tactical"] = ["Stratégie"], ["Card & Board Game"] = ["Stratégie"],
        ["Shooter"] = ["Action"], ["Fighting"] = ["Action"], ["Hack and slash/Beat 'em up"] = ["Action"], ["Platform"] = ["Action"],
        ["MOBA"] = ["Action", "Stratégie"],
        ["Arcade"] = ["Occasionnel"], ["Puzzle"] = ["Occasionnel"], ["Quiz/Trivia"] = ["Occasionnel"], ["Music"] = ["Occasionnel"],
        ["Sport"] = ["Sport"], ["Racing"] = ["Course automobile"],
    };

    private static List<string> GalaxyGenres(string? metaJson)
    {
        if (metaJson is null) return [];
        using var document = JsonDocument.Parse(metaJson);
        return document.RootElement.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array
            ? genres.EnumerateArray().SelectMany(g => GalaxyGenreMap.GetValueOrDefault(g.GetString() ?? "") ?? []).Distinct().ToList()
            : [];
    }

    /// <summary>Magasin d'un profil, déduit de son lancement (pour le filtre « Plateforme » des jeux installés).</summary>
    public static GameSource? StoreOf(GameProfile profile, bool isSteamGame)
    {
        if (isSteamGame) return GameSource.Steam;
        var launcher = Path.GetFileName(profile.LauncherPath ?? "");
        return launcher.ToLowerInvariant() switch
        {
            "epicgameslauncher.exe" => GameSource.Epic,
            "galaxyclient.exe" => GameSource.Gog,
            _ => null,
        };
    }

    public static string Label(GameSource store) => store switch
    {
        GameSource.Steam => "Steam",
        GameSource.Epic => "Epic Games",
        GameSource.Gog => "GOG",
        _ => "Autre",
    };

    /// <summary>« The Sims™ 3 » et « The Sims 3 » → « thesims3 » (comparaison des titres entre sources).</summary>
    public static string NameKey(string name) => string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? JsonText(string? json, string name)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        return Text(document.RootElement, name);
    }
}
