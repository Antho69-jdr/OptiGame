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

    private readonly Dictionary<string, IReadOnlyList<string>?> _openIssues = [];

    /// <summary>Taille maximale acceptée pour le PDF des notes de version (≈ 600 Ko relevés).</summary>
    private const long MaxReleaseNotesBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Problèmes encore ouverts d'une version, lus dans le PDF de ses notes de version (serveurs de téléchargement de NVIDIA
    /// seulement, gardés en mémoire par version) ; null si le PDF manque, est illisible ou a changé de format.
    /// </summary>
    public async Task<IReadOnlyList<string>?> OpenIssuesAsync(NvidiaDriver driver, CancellationToken cancellation = default)
    {
        if (_openIssues.TryGetValue(driver.Version, out var known)) return known;
        IReadOnlyList<string>? issues = null;
        if (driver.ReleaseNotesPdf is { } pdf && NvidiaDrivers.IsOfficialDownload(pdf))
        {
            try
            {
                using var response = await Http.GetAsync(pdf, HttpCompletionOption.ResponseHeadersRead, cancellation);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { } final && !NvidiaDrivers.IsOfficialDownload(final))
                {
                    throw new HttpRequestException($"Redirection hors des serveurs de NVIDIA : {final}");
                }
                if (response.Content.Headers.ContentLength > MaxReleaseNotesBytes) throw new HttpRequestException("Notes de version trop volumineuses.");
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
                issues = NvidiaReleaseNotes.OpenIssues(PdfText.Extract(bytes), driver.Version);
                log.Info(issues is null
                    ? $"Notes de version NVIDIA {driver.Version} : section des problèmes ouverts introuvable (format changé ?)."
                    : $"Notes de version NVIDIA {driver.Version} : {issues.Count} problème(s) encore ouvert(s).");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.Warn($"Notes de version NVIDIA {driver.Version} illisibles : {ex.Message}");
                return null; // pas mémorisé : nouvel essai à la prochaine recherche
            }
        }
        _openIssues[driver.Version] = issues;
        return issues;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
