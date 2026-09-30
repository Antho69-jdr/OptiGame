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

    public async Task<string> DownloadNvidiaInstallerAsync(Uri url, IProgress<DownloadProgress> progress, CancellationToken cancellation)
    {
        var name = InstallerFiles.FileNameFor(url) ?? throw new InstallerRejectedException($"Adresse de téléchargement refusée : {url}");
        Directory.CreateDirectory(paths.DownloadsDir);
        var target = Path.Combine(paths.DownloadsDir, name);

        if (File.Exists(target) && Check(target) is null)
        {
            log.Info($"Installeur déjà téléchargé et vérifié, réutilisé : {target}");
            return target;
        }

        var part = target + ".part";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            // Une redirection ne doit pas mener hors des serveurs de NVIDIA.
            if (response.RequestMessage?.RequestUri is { } final && !NvidiaDrivers.IsOfficialDownload(final))
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

        if (Check(target) is { } problem)
        {
            // Jamais ouvert ; gardé sous un nom non exécutable pour examen, comme tout fichier rejeté.
            File.Move(target, target + ".non-verifie", overwrite: true);
            log.Error($"Installeur rejeté ({problem}) : {target}.non-verifie");
            throw new InstallerRejectedException(problem);
        }

        foreach (var old in Directory.EnumerateFiles(paths.DownloadsDir, "*.exe").Where(f => !f.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            TryDelete(old);
        }
        log.Info($"Installeur NVIDIA téléchargé et vérifié : {target}");
        return target;
    }

    /// <summary>Null si la signature est valide et vient de NVIDIA Corporation ; sinon la raison du refus.</summary>
    public static string? Check(string path)
    {
        var signature = AuthenticodeVerifier.Verify(path);
        if (!signature.IsValid) return signature.Detail;
        return InstallerSignature.IsNvidia(signature.Signer) ? null : $"Signataire inattendu : {signature.Signer}";
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
