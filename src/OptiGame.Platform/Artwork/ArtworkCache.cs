using OptiGame.Core;
using OptiGame.Core.Artwork;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Artwork;

/// <summary>
/// Images IGDB en cache dans %LocalAppData%\OptiGame\covers : une image n'est téléchargée qu'une fois.
/// Écriture dans un fichier temporaire puis renommage, pour ne jamais laisser une image tronquée.
/// </summary>
public sealed class ArtworkCache(AppPaths paths, FileLog log)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string Directory => Path.Combine(paths.Root, "covers");

    public string PathFor(string imageId, string size) =>
        Path.Combine(Directory, $"{string.Concat(imageId.Where(char.IsLetterOrDigit))}_{size}.jpg");

    /// <summary>Fonds choisis par l'utilisateur (« Changer le fond… »), un fichier par choix.</summary>
    public string HeroesDirectory => Path.Combine(Directory, "heroes");

    /// <summary>Chemin du fond choisi, s'il existe encore (null sinon : la fiche reprend le fond d'origine).</summary>
    public string? CustomHeroPath(string? file) =>
        file is not null && File.Exists(Path.Combine(HeroesDirectory, file)) ? Path.Combine(HeroesDirectory, file) : null;

    /// <summary>
    /// Copie une image choisie comme fond (image IGDB en cache, bannière de Steam, fichier du PC) et renvoie son nom. Nom
    /// nouveau à chaque choix : l'ancienne image décodée en mémoire n'est jamais réaffichée par erreur.
    /// </summary>
    public string StoreCustomHero(Guid profileId, string sourcePath)
    {
        System.IO.Directory.CreateDirectory(HeroesDirectory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant() is ".png" ? ".png" : ".jpg";
        var file = $"{profileId:N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        var path = Path.Combine(HeroesDirectory, file);
        File.Copy(sourcePath, path + ".tmp", overwrite: true);
        File.Move(path + ".tmp", path, overwrite: true);
        return file;
    }

    /// <summary>Supprime un fond qui n'est plus utilisé (erreur journalisée, jamais bloquante).</summary>
    public void DeleteCustomHero(string? file)
    {
        if (file is null || Path.GetFileName(file) != file) return; // jamais en dehors du dossier des fonds
        try
        {
            File.Delete(Path.Combine(HeroesDirectory, file));
        }
        catch (IOException ex)
        {
            log.Warn($"Ancien fond {file} non supprimé : {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Warn($"Ancien fond {file} non supprimé : {ex.Message}");
        }
    }

    /// <summary>Chemin local de l'image si elle est déjà en cache, sinon null (aucun accès réseau).</summary>
    public string? TryGetCached(string? imageId, string size) =>
        imageId is not null && File.Exists(PathFor(imageId, size)) ? PathFor(imageId, size) : null;

    /// <summary>Chemin local de l'image, téléchargée si besoin ; null si elle est indisponible.</summary>
    public async Task<string?> GetAsync(string? imageId, string size, CancellationToken cancellation = default)
    {
        if (imageId is null) return null;
        var path = PathFor(imageId, size);
        if (File.Exists(path)) return path;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var bytes = await Http.GetByteArrayAsync(Igdb.ImageUrl(imageId, size), cancellation);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellation);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            log.Warn($"Image IGDB {imageId} ({size}) indisponible : {ex.Message}");
            return null;
        }
    }
}
