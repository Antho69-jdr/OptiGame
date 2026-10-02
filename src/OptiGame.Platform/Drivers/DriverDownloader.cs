using OptiGame.Core;
using OptiGame.Core.Drivers;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Drivers;

public sealed record DownloadProgress(long Received, long? Total);

/// <summary>Installeur refusé : signature invalide ou signataire autre que NVIDIA. Le fichier n'est jamais ouvert.</summary>
public sealed class InstallerRejectedException(string message) : Exception(message);

/// <summary>
/// Téléchargement de l'installeur officiel NVIDIA dans %LocalAppData%\OptiGame\downloads, puis vérification de sa
/// signature. Un installeur déjà téléchargé et toujours valide est réutilisé (≈ 1 Go). Seul le dernier est gardé.
/// </summary>
public sealed class DriverDownloader(AppPaths paths, FileLog log)
{
    private static readonly HttpClient Http = CreateClient();

    public Task<string> DownloadNvidiaInstallerAsync(Uri url, IProgress<DownloadProgress> progress, CancellationToken cancellation) =>
        DownloadInstallerAsync(InstallerVendor.Nvidia, url, progress, cancellation);

    /// <summary>
    /// Installeur officiel d'un fabricant, dans downloads&lt;fabricant&gt; (le nettoyage des anciennes versions ne touche pas
    /// aux installeurs des autres fabricants). La page d'origine est annoncée quand le serveur l'exige (AMD).
    /// </summary>
    public async Task<string> DownloadInstallerAsync(InstallerVendor vendor, Uri url, IProgress<DownloadProgress> progress, CancellationToken cancellation)
    {
        var name = OfficialInstallers.FileNameFor(vendor, url) ?? throw new InstallerRejectedException($"Adresse de téléchargement refusée : {url}");
        var folder = Path.Combine(paths.DownloadsDir, vendor.ToString().ToLowerInvariant());
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, name);

        if (File.Exists(target) && Check(target, vendor) is null)
        {
            log.Info($"Installeur déjà téléchargé et vérifié, réutilisé : {target}");
            return target;
        }

        var part = target + ".part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (OfficialInstallers.Referer(vendor) is { } referer) request.Headers.Referrer = referer;
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            // Une redirection ne doit pas mener hors des serveurs du fabricant (sans Referer, AMD redirige vers une page HTML).
            if (response.RequestMessage?.RequestUri is { } final && !OfficialInstallers.IsOfficialDownload(vendor, final))
            {
                throw new InstallerRejectedException($"Redirection vers une adresse non officielle refusée : {final}");
            }

            var total = response.Content.Headers.ContentLength;
            long received = 0;
            var lastReport = DateTime.MinValue;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    received += read;
                    if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(200))
                    {
                        lastReport = DateTime.UtcNow;
                        progress.Report(new DownloadProgress(received, total));
                    }
                }
            }
            progress.Report(new DownloadProgress(received, total));
            if (total is { } expected && received != expected)
            {
                throw new IOException($"Téléchargement incomplet : {received} octets reçus sur {expected}.");
            }

            File.Move(part, target, overwrite: true);
        }
        catch
        {
            TryDelete(part); // téléchargement interrompu ou annulé : rien d'utilisable
            throw;
        }

        if (Check(target, vendor) is { } problem)
        {
            // Jamais ouvert ; gardé sous un nom non exécutable pour examen, comme tout fichier rejeté.
            File.Move(target, target + ".non-verifie", overwrite: true);
            log.Error($"Installeur rejeté ({problem}) : {target}.non-verifie");
            throw new InstallerRejectedException(problem);
        }

        foreach (var old in Directory.EnumerateFiles(folder, "*.exe").Where(f => !f.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            TryDelete(old);
        }
        log.Info($"Installeur {OfficialInstallers.SignerName(vendor)} téléchargé et vérifié : {target}");
        return target;
    }

    /// <summary>Null si la signature est valide et vient du fabricant attendu ; sinon la raison du refus.</summary>
    public static string? Check(string path, InstallerVendor vendor)
    {
        var signature = AuthenticodeVerifier.Verify(path);
        if (!signature.IsValid) return signature.Detail;
        return OfficialInstallers.IsExpectedSigner(vendor, signature.Signer) ? null : $"Signataire inattendu : {signature.Signer}";
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            log.Warn($"Suppression impossible de {path} : {ex.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // ≈ 1 Go : pas de délai global, l'annulation suffit
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
