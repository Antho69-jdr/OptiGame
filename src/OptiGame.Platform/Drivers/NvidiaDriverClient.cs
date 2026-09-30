using OptiGame.Core.Abstractions;
using OptiGame.Core.Drivers;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Drivers;

/// <summary>
/// Dernier pilote NVIDIA publié pour une carte (lecture seule, à la demande). Seul le modèle de la carte est envoyé.
/// La liste des produits NVIDIA (≈ 90 Ko) est gardée en mémoire pendant l'exécution.
/// </summary>
public sealed class NvidiaDriverClient(FileLog log)
{
    private static readonly HttpClient Http = CreateClient();
    private string? _productList;

    public async Task<GpuDriverStatus> CheckAsync(GpuAdapter gpu, CancellationToken cancellation = default)
    {
        if (DriverRules.VendorOf(gpu.PnpDeviceId) != GpuVendor.Nvidia) return DriverRules.Evaluate(gpu, null);
        try
        {
            _productList ??= await Http.GetStringAsync(NvidiaDrivers.ProductListUri, cancellation);
            if (NvidiaDrivers.FindProduct(gpu.Name, _productList) is not { } product)
            {
                return DriverRules.Evaluate(gpu, null, $"« {gpu.Name} » est absente de la liste des produits NVIDIA.");
            }

            var json = await Http.GetStringAsync(NvidiaDrivers.LookupUri(product.SeriesId, product.ProductId), cancellation);
            var latest = NvidiaDrivers.ParseLookup(json);
            if (latest is not null && !NvidiaDrivers.IsOfficialDownload(latest.DownloadUrl))
            {
                log.Warn($"Pilote NVIDIA ignoré : adresse de téléchargement non officielle ({latest.DownloadUrl}).");
                latest = null;
            }
            log.Info($"Pilote NVIDIA pour « {gpu.Name} » (psid {product.SeriesId}, pfid {product.ProductId}) : " +
                     $"installé {gpu.DriverVersion}, dernier {latest?.Version ?? "introuvable"}.");
            return DriverRules.Evaluate(gpu, latest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or System.Xml.XmlException)
        {
            log.Error($"Recherche du pilote NVIDIA pour « {gpu.Name} » impossible", ex);
            return DriverRules.Evaluate(gpu, null, $"Service de NVIDIA injoignable : {ex.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
