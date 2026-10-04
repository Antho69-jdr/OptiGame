using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using OptiGame.Core.Logging;
using OptiGame.Core.Updates;
using OptiGame.Platform.Drivers;

namespace OptiGame.Platform.Updates;

/// <summary>
/// Versions d'OptiGame publiées sur GitHub : dernière version (API sans compte, limitée à 60 requêtes par heure et par adresse
/// IP : une recherche par jour suffit) et téléchargement vérifié de son installeur (taille et SHA-256 publiés par GitHub).
/// </summary>
public sealed class AppUpdateClient(FileLog log)
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Compare la dernière version publiée à <paramref name="current"/>. Lève <see cref="HttpRequestException"/> sans réseau.</summary>
    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(CheckTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, AppReleases.LatestApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await Http.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return AppReleases.Evaluate(null, current); // aucune version publiée
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("GitHub limite pour l'instant les recherches depuis cette connexion : nouvel essai plus tard.");
        }
        response.EnsureSuccessStatusCode();
        return AppReleases.Evaluate(await response.Content.ReadAsStringAsync(timeout.Token), current);
    }

    /// <summary>
    /// Télécharge l'installeur dans <paramref name="folder"/> (le dossier des mises à jour, réservé aux administrateurs) et le
    /// vérifie : redirection vers les serveurs de GitHub seulement, taille et SHA-256 publiés. Un installeur déjà téléchargé et
    /// toujours valide est réutilisé. Fichier refusé = renommé « .non-verifie », jamais ouvert.
    /// </summary>
    public async Task<string> DownloadAsync(UpdatePackage package, string folder, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, package.InstallerName);
        if (File.Exists(target) && Sha256Of(target) == package.Sha256)
        {
            log.Info($"Mise à jour {package.Version.ToString(3)} déjà téléchargée et vérifiée : {target}");
            return target;
        }

        var part = target + ".part";
        long received = 0;
        try
        {
            using var response = await Http.GetAsync(package.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is not { } final || !AppReleases.IsAllowedDownload(final))
            {
                throw new InstallerRejectedException($"Téléchargement redirigé hors des serveurs de GitHub ({response.RequestMessage?.RequestUri?.Host}) : refusé.");
            }
            if (response.Content.Headers.ContentLength is { } announced && announced != package.InstallerSize)
            {
                throw new InstallerRejectedException($"Taille annoncée par le serveur ({announced} octets) différente de celle publiée ({package.InstallerSize}).");
            }

            var lastReport = DateTime.MinValue;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    received += read;
                    if (received > package.InstallerSize) throw new InstallerRejectedException("Fichier plus gros que l'installeur publié : refusé.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(200))
                    {
                        lastReport = DateTime.UtcNow;
                        progress?.Report(new DownloadProgress(received, package.InstallerSize));
                    }
                }
            }
            progress?.Report(new DownloadProgress(received, package.InstallerSize));
        }
        catch
        {
            TryDelete(part); // interrompu, annulé ou refusé : rien d'utilisable
            throw;
        }

        if (received != package.InstallerSize)
        {
            TryDelete(part);
            throw new IOException($"Téléchargement incomplet : {received} octets reçus sur {package.InstallerSize}.");
        }
        var actual = Sha256Of(part);
        if (actual != package.Sha256)
        {
            File.Move(part, target + ".non-verifie", overwrite: true);
            log.Error($"Mise à jour rejetée : SHA-256 {actual} au lieu de {package.Sha256} publié par GitHub ({target}.non-verifie).");
            throw new InstallerRejectedException("L'installeur téléchargé ne correspond pas à celui publié (empreinte SHA-256 différente) : refusé, jamais ouvert.");
        }
        File.Move(part, target, overwrite: true);

        foreach (var old in Directory.EnumerateFiles(folder).Where(f => !f.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            TryDelete(old); // installeurs précédents, fichiers rejetés : seul le dernier vérifié est gardé
        }
        log.Info($"Mise à jour {package.Version.ToString(3)} téléchargée et vérifiée (SHA-256 {actual[..16]}…) : {target}");
        return target;
    }

    /// <summary>Supprime les installeurs déjà utilisés (version installée ou plus ancienne) du dossier des mises à jour.</summary>
    public void CleanUp(string folder, Version installed)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            var version = AppReleases.IsInstallerName(name) ? AppReleases.ParseTag("v" + name["OptiGame-Setup-".Length..^".exe".Length]) : null;
            if (version is null || version <= installed) TryDelete(file);
        }
    }

    public static string Sha256Of(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Suppression impossible de {path} : {ex.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // ≈ 50 Mo : l'annulation suffit
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"OptiGame/{version}"); // exigé par l'API de GitHub
        return client;
    }
}
