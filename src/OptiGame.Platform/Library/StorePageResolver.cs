using System.Net.Http.Headers;
using System.Text.Json;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Library;

/// <summary>
/// « Voir sur Epic Games / GOG » : adresse de la page du jeu, demandée au clic (rien n'est envoyé d'autre que l'identifiant du
/// produit à GOG ; la table d'Epic est publique et lue une fois par session). Service indisponible ou jeu absent = recherche du
/// titre sur le magasin : le bouton marche toujours.
/// </summary>
public sealed class StorePageResolver(FileLog log)
{
    private static readonly HttpClient Http = CreateClient();
    private string? _epicMapping;

    public async Task<string> UrlAsync(StoreProduct product, CancellationToken cancellation = default)
    {
        try
        {
            if (product.Store == GameSource.Epic)
            {
                _epicMapping ??= await Http.GetStringAsync(StorePages.EpicMappingUrl, cancellation);
                if (StorePages.EpicSlug(_epicMapping, product.Id) is { } slug) return StorePages.EpicPageUrl(slug);
            }
            else if (product.Store == GameSource.Gog)
            {
                var json = await Http.GetStringAsync(StorePages.GogProductApiUrl(product.Id), cancellation);
                if (StorePages.GogProductCard(json) is { } url) return url;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.Warn($"Page {product.Store} de « {product.Title} » introuvable ({ex.Message}) : recherche sur le magasin.");
        }
        return StorePages.SearchUrl(product);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OptiGame", "1.0"));
        return client;
    }
}
