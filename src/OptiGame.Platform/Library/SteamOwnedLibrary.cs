using OptiGame.Core.Library;

namespace OptiGame.Platform.Library;

/// <summary>Bibliothèque Steam lue dans les caches du client : jeux possédés (installés ou non) par appid.</summary>
/// <param name="GameAppIds">Applications de type « game » d'appinfo.vdf, possédées ou non : un jeu installé en est un, un outil
/// (SaveSync…) non. Sans exiger de licence ni d'images en cache (un jeu tout juste installé peut ne pas en avoir).</param>
public sealed record SteamOwnedSnapshot(IReadOnlyDictionary<uint, OwnedSteamGame> Games, IReadOnlySet<uint> GameAppIds)
{
    public bool IsGame(uint appId) => GameAppIds.Contains(appId);
}

/// <summary>
/// Lecture seule des caches du client Steam (<c>appcache\appinfo.vdf</c>, <c>packageinfo.vdf</c>, <c>librarycache\</c>) : jeux
/// possédés, genres, catégories, et jaquettes déjà téléchargées par Steam (rien n'est téléchargé). Lu en ~0,1 s ; à appeler hors
/// du thread UI. <see cref="FormatException"/> si Steam a changé de format de cache.
/// </summary>
public static class SteamOwnedLibrary
{
    private static readonly Lock Gate = new();
    private static (string Stamp, SteamOwnedSnapshot Snapshot)? _last;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, string> Covers = new();

    /// <summary>
    /// Null si Steam n'est pas installé ou n'a pas encore de cache. Relu seulement si appinfo.vdf, packageinfo.vdf ou le
    /// dossier librarycache ont changé depuis la dernière lecture (date et taille) : sinon la lecture précédente est rendue.
    /// </summary>
    public static SteamOwnedSnapshot? Read()
    {
        if (CacheDir() is not { } cache) return null;
        var appInfo = Path.Combine(cache, "appinfo.vdf");
        var packageInfo = Path.Combine(cache, "packageinfo.vdf");
        var library = Path.Combine(cache, "librarycache");
        if (!File.Exists(appInfo) || !File.Exists(packageInfo) || !Directory.Exists(library)) return null;

        var stamp = FileStamps.Of(appInfo, packageInfo, library);
        lock (Gate)
        {
            if (_last is { } last && last.Stamp == stamp) return last.Snapshot;
        }
        var snapshot = ReadNow(library, appInfo, packageInfo);
        lock (Gate)
        {
            _last = (stamp, snapshot);
        }
        return snapshot;
    }

    private static SteamOwnedSnapshot ReadNow(string library, string appInfo, string packageInfo)
    {

        var inLibrary = Directory.EnumerateDirectories(library)
            .Select(d => uint.TryParse(Path.GetFileName(d), out var id) ? id : 0)
            .Where(id => id != 0)
            .ToHashSet();
        var apps = SteamBinaryCache.ParseAppInfo(ReadShared(appInfo));
        var owned = SteamOwnedGames.Find(apps, SteamBinaryCache.ParsePackageInfo(ReadShared(packageInfo)), inLibrary);
        return new SteamOwnedSnapshot(owned.ToDictionary(g => g.AppId), apps.Where(a => a.Type == "game").Select(a => a.AppId).ToHashSet());
    }

    /// <summary>Jaquette portrait mise en cache par Steam (300×450) : <c>library_600x900.jpg</c>, ou <c>library_capsule.jpg</c>
    /// dans un sous-dossier (forme récente du cache, ex. Battlefield 6). Null si Steam ne l'a pas.</summary>
    public static string? CoverPath(uint appId)
    {
        // Seules les jaquettes trouvées sont retenues : une jaquette que Steam télécharge plus tard sera vue à la lecture suivante.
        if (Covers.TryGetValue(appId, out var known) && File.Exists(known)) return known;
        // Noms traduits aussi (« library_capsule_french.jpg » : Overwatch, vu le 2026-10-10).
        var path = Find(appId, "library_600x900.jpg") ?? Find(appId, "library_capsule.jpg") ??
                   Find(appId, "library_600x900_*.jpg") ?? Find(appId, "library_capsule_*.jpg");
        if (path is not null) Covers[appId] = path;
        return path;
    }

    /// <summary>Bannière large que Steam affiche en haut de la page du jeu (<c>library_hero.jpg</c>, souvent 3840×1240), si Steam l'a
    /// en cache : proposée comme fond de la fiche, rien n'est téléchargé.</summary>
    public static string? HeroPath(uint appId) => Find(appId, "library_hero.jpg");

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
        using var memory = new MemoryStream((int)Math.Min(stream.Length, Array.MaxLength)); // taille connue : pas d'agrandissements successifs
        stream.CopyTo(memory);
        return memory.Length == memory.Capacity ? memory.GetBuffer() : memory.ToArray();
    }
}
