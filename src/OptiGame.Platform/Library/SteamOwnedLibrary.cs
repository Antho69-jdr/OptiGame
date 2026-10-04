using OptiGame.Core.Library;

namespace OptiGame.Platform.Library;

/// <summary>Bibliothèque Steam lue dans les caches du client : jeux possédés (installés ou non) par appid.</summary>
public sealed record SteamOwnedSnapshot(IReadOnlyDictionary<uint, OwnedSteamGame> Games);

/// <summary>
/// Lecture seule des caches du client Steam (<c>appcache\appinfo.vdf</c>, <c>packageinfo.vdf</c>, <c>librarycache\</c>) : jeux
/// possédés, genres, catégories, et jaquettes déjà téléchargées par Steam (rien n'est téléchargé). Lu en ~0,1 s ; à appeler hors
/// du thread UI. <see cref="FormatException"/> si Steam a changé de format de cache.
/// </summary>
public static class SteamOwnedLibrary
{
    /// <summary>Null si Steam n'est pas installé ou n'a pas encore de cache.</summary>
    public static SteamOwnedSnapshot? Read()
    {
        if (CacheDir() is not { } cache) return null;
        var appInfo = Path.Combine(cache, "appinfo.vdf");
        var packageInfo = Path.Combine(cache, "packageinfo.vdf");
        var library = Path.Combine(cache, "librarycache");
        if (!File.Exists(appInfo) || !File.Exists(packageInfo) || !Directory.Exists(library)) return null;

        var inLibrary = Directory.EnumerateDirectories(library)
            .Select(d => uint.TryParse(Path.GetFileName(d), out var id) ? id : 0)
            .Where(id => id != 0)
            .ToHashSet();
        var owned = SteamOwnedGames.Find(
            SteamBinaryCache.ParseAppInfo(ReadShared(appInfo)), SteamBinaryCache.ParsePackageInfo(ReadShared(packageInfo)), inLibrary);
        return new SteamOwnedSnapshot(owned.ToDictionary(g => g.AppId));
    }

    /// <summary>Jaquette portrait mise en cache par Steam (300×450) : <c>library_600x900.jpg</c>, ou <c>library_capsule.jpg</c>
    /// dans un sous-dossier (forme récente du cache, ex. Battlefield 6). Null si Steam ne l'a pas.</summary>
    public static string? CoverPath(uint appId) =>
        Find(appId, "library_600x900.jpg") ?? Find(appId, "library_capsule.jpg");

    private static string? Find(uint appId, string fileName)
    {
        if (CacheDir() is not { } cache) return null;
        var folder = Path.Combine(cache, "librarycache", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, fileName, SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    private static string? CacheDir() => GameLibraryScanner.SteamPath() is { } steam ? Path.Combine(steam, "appcache") : null;

    /// <summary>Steam peut écrire ces fichiers pendant la lecture : partage en lecture/écriture, jamais de verrou.</summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
