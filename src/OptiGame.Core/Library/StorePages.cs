using System.Text.Json;
using System.Text.RegularExpressions;
using OptiGame.Core.Profiles;

namespace OptiGame.Core.Library;

/// <summary>Jeu repéré dans le magasin Epic (espace de noms du catalogue) ou GOG (identifiant produit), pour « Voir sur … ».</summary>
public sealed record StoreProduct(GameSource Store, string Id, string Title);

/// <summary>
/// Page d'un jeu dans le magasin Epic Games ou GOG (« Voir sur Epic Games / GOG »), vérifié le 2026-10-06 :
/// <list type="bullet">
/// <item>Epic : le catalogue local (catcache.bin) ne contient PAS l'identifiant de page ; le service public
/// <c>store-content.ak.epicgames.com/api/content/productmapping</c> (JSON « espace de noms → page », 1283 entrées) donne la page de
/// 231 des 348 jeux de la machine de dev (Absolute Drift → « absolute-drift »). Sinon : recherche du titre sur le magasin.</item>
/// <item>GOG : la base de Galaxy n'a pas l'adresse ; <c>api.gog.com/products/&lt;id&gt;</c> renvoie <c>links.product_card</c>
/// (Vambrace: Cold Soul → www.gog.com/game/vambrace_cold_soul). Sinon : recherche du titre.</item>
/// </list>
/// </summary>
public static partial class StorePages
{
    public const string EpicMappingUrl = "https://store-content.ak.epicgames.com/api/content/productmapping";

    public static string GogProductApiUrl(string productId) =>
        IsDigits(productId) ? $"https://api.gog.com/products/{productId}" : throw new ArgumentException($"Produit GOG invalide : « {productId} ».");

    /// <summary>Clé d'un jeu Epic possédé (« espace de noms:élément:appli ») ou releaseKey GOG (« gog_123 ») → produit.</summary>
    public static StoreProduct? FromOwned(GameSource store, string key, string title) => store switch
    {
        GameSource.Epic when key.Split(':') is [var ns, _, _] && IsId(ns) => new StoreProduct(GameSource.Epic, ns, title),
        GameSource.Gog when key.StartsWith("gog_", StringComparison.Ordinal) && IsDigits(key[4..]) => new StoreProduct(GameSource.Gog, key[4..], title),
        _ => null,
    };

    /// <summary>Profil créé depuis Epic ou GOG (commande exacte des raccourcis des lanceurs, StoreLaunchers) → produit.</summary>
    public static StoreProduct? FromProfile(GameProfile profile)
    {
        var arguments = profile.LaunchArguments ?? "";
        if (EpicApps().Match(arguments) is { Success: true } epic) return new StoreProduct(GameSource.Epic, epic.Groups[1].Value, profile.Name);
        if (GogGame().Match(arguments) is { Success: true } gog) return new StoreProduct(GameSource.Gog, gog.Groups[1].Value, profile.Name);
        return null;
    }

    /// <summary>Réponse de productmapping → page Epic de cet espace de noms, ou null.</summary>
    public static string? EpicSlug(string mappingJson, string catalogNamespace)
    {
        using var document = JsonDocument.Parse(mappingJson);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(catalogNamespace, out var slug)
               && slug.ValueKind == JsonValueKind.String && slug.GetString() is { Length: > 0 } text && SlugPattern().IsMatch(text)
            ? text
            : null;
    }

    /// <summary>Page du magasin Epic (en français).</summary>
    public static string EpicPageUrl(string slug) => $"https://store.epicgames.com/fr/p/{slug}";

    /// <summary>Réponse de l'API produit GOG → adresse de la page, seulement si elle est bien sur www.gog.com.</summary>
    public static string? GogProductCard(string productJson)
    {
        using var document = JsonDocument.Parse(productJson);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("links", out var links)
               && links.ValueKind == JsonValueKind.Object && links.TryGetProperty("product_card", out var card) && card.ValueKind == JsonValueKind.String
               && Uri.TryCreate(card.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "www.gog.com"
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>Page introuvable : recherche du titre sur le magasin (toujours utilisable).</summary>
    public static string SearchUrl(StoreProduct product) => product.Store == GameSource.Epic
        ? $"https://store.epicgames.com/fr/browse?q={Uri.EscapeDataString(product.Title)}&sortBy=relevancy&sortDir=DESC"
        : $"https://www.gog.com/fr/games?query={Uri.EscapeDataString(product.Title)}";

    public static string Label(GameSource store) => store == GameSource.Epic ? "Voir sur Epic Games" : "Voir sur GOG";

    private static bool IsDigits(string text) => text.Length is > 0 and <= 20 && text.All(char.IsAsciiDigit);

    private static bool IsId(string text) => text.Length is > 0 and <= 64 && text.All(char.IsAsciiLetterOrDigit);

    [GeneratedRegex(@"com\.epicgames\.launcher://apps/([A-Za-z0-9]{1,64})%3A", RegexOptions.IgnoreCase)]
    private static partial Regex EpicApps();

    [GeneratedRegex(@"/gameId=(\d{1,20})\b")]
    private static partial Regex GogGame();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-_.]{0,127}$", RegexOptions.IgnoreCase)]
    private static partial Regex SlugPattern();
}
