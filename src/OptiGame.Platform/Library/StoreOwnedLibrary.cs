using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OptiGame.Core;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Library;

/// <summary>
/// Jeux possédés des autres magasins (lecture seule) : catalogue d'Epic (catcache.bin) et bibliothèque de GOG Galaxy
/// (galaxy-2.0.db, SQLite en mode WAL). La base de Galaxy n'est JAMAIS ouverte en place : elle est copiée (avec ses fichiers
/// -wal et -shm) dans un dossier temporaire, lue, puis la copie est supprimée — Galaxy peut tourner pendant ce temps.
/// Une source illisible est ignorée (journalisée) sans empêcher les autres.
/// </summary>
public sealed class StoreOwnedLibrary(FileLog log)
{
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static string EpicCatalogPath => Path.Combine(ProgramData, "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");

    public static string GalaxyDatabasePath => Path.Combine(ProgramData, "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");

    /// <summary>Jeux possédés et non installés (Epic, GOG, Ubisoft et EA via Galaxy). Fichiers et SQLite : hors du thread UI.</summary>
    public IReadOnlyList<StoreOwnedGame> ReadNotInstalled()
    {
        var owned = new List<StoreOwnedGame>();
        try
        {
            if (File.Exists(EpicCatalogPath)) owned.AddRange(StoreCatalogs.ParseEpicCatalog(ReadShared(EpicCatalogPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            log.Warn($"Catalogue Epic illisible : {ex.Message}");
        }
        try
        {
            if (File.Exists(GalaxyDatabasePath)) owned.AddRange(StoreCatalogs.FromGalaxy(ReadGalaxyRows()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or System.Text.Json.JsonException)
        {
            log.Warn($"Bibliothèque de GOG Galaxy illisible : {ex.Message}");
        }

        // Installés : Epic par identifiant de catalogue, GOG par identifiant, Ubisoft et EA par titre (Galaxy ne donne pas leur numéro).
        var installed = StoreLibraries.ScanInstalled().ToList();
        var installedNames = installed.Select(g => StoreCatalogs.NameKey(g.Name)).ToHashSet();
        var installedKeys = installed.Select(g => g.LaunchArguments ?? "").ToList();
        return owned
            .Where(g => !installedNames.Contains(StoreCatalogs.NameKey(g.Name)))
            .Where(g => g.Store != GameSource.Epic || !installedKeys.Any(a => a.Contains(g.Key.Replace(":", "%3A"), StringComparison.Ordinal)))
            .Where(g => g.Store != GameSource.Gog || !installedKeys.Any(a => a.Contains($"/gameId={g.Key[4..]} ", StringComparison.Ordinal)))
            .ToList();
    }

    private static IEnumerable<GalaxyRow> ReadGalaxyRows()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"optigame-galaxy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(copy);
        try
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(GalaxyDatabasePath + suffix))
                {
                    File.WriteAllBytes(Path.Combine(copy, "galaxy-2.0.db" + suffix), ReadShared(GalaxyDatabasePath + suffix));
                }
            }
            var rows = new List<GalaxyRow>();
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(copy, "galaxy-2.0.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    select lr.releaseKey, ifnull(rp.isDlc, 0), ifnull(rp.isVisibleInLibrary, 1),
                           (select value from GamePieces where releaseKey = lr.releaseKey and gamePieceTypeId = (select id from GamePieceTypes where type = 'title')),
                           (select value from GamePieces where releaseKey = lr.releaseKey and gamePieceTypeId = (select id from GamePieceTypes where type = 'originalImages')),
                           (select value from GamePieces where releaseKey = lr.releaseKey and gamePieceTypeId = (select id from GamePieceTypes where type = 'originalMeta'))
                    from LibraryReleases lr left join ReleaseProperties rp on rp.releaseKey = lr.releaseKey
                    where lr.releaseKey like 'gog!_%' escape '!' or lr.releaseKey like 'uplay!_%' escape '!' or lr.releaseKey like 'origin!_%' escape '!'
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new GalaxyRow(reader.GetString(0), reader.GetInt64(1) != 0, reader.GetInt64(2) != 0,
                        reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
            }
            return rows;
        }
        finally
        {
            try
            {
                Directory.Delete(copy, recursive: true);
            }
            catch (IOException)
            {
                // copie temporaire : nettoyée par Windows sinon
            }
        }
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>
/// Jaquettes des jeux Epic et GOG (CDN publics : seule l'adresse de l'image est demandée), en cache dans covers\stores : une image
/// n'est téléchargée qu'une fois. Seuls les domaines cdn1.epicgames.com et images.gog.com sont acceptés.
/// </summary>
public sealed class StoreCoverCache(AppPaths paths, FileLog log)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string[] AllowedHosts = ["cdn1.epicgames.com", "images.gog.com"];
    private readonly SemaphoreSlim _parallel = new(4);

    private string Directory => Path.Combine(paths.Root, "covers", "stores");

    public string PathFor(string url) =>
        Path.Combine(Directory, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..20] + ".jpg");

    public string? TryGetCached(string? url) => url is not null && File.Exists(PathFor(url)) ? PathFor(url) : null;

    public async Task<string?> GetAsync(string? url, CancellationToken cancellation = default)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(uri.Host)) return null;
        var path = PathFor(url);
        if (File.Exists(path)) return path;
        await _parallel.WaitAsync(cancellation);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var bytes = await Http.GetByteArrayAsync(uri, cancellation);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellation);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            log.Warn($"Jaquette indisponible ({uri.Host}) : {ex.Message}");
            return null;
        }
        finally
        {
            _parallel.Release();
        }
    }
}
