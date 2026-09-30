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
