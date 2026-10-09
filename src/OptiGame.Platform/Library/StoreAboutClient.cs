using OptiGame.Core;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.State;

namespace OptiGame.Platform.Library;

public sealed class StoreAboutCacheEntry
{
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Réponses brutes de Steam (appdetails filtré, appreviews), relues à chaque fois : le format reste vérifiable.
    /// Vide = pas encore obtenue.</summary>
    public string DetailsJson { get; set; } = "";

    public string ReviewsJson { get; set; } = "";
}

public sealed class StoreAboutCache
{
    /// <summary>2 : réponses avec les bandes-annonces (filtre movies) ; les entrées de la version 1 sont relues sur Steam.</summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 2;

    /// <summary>Clé : appid Steam, « gog:    /// <summary>Clé : appid Steam.</summary>lt;produit    /// <summary>Clé : appid Steam.</summary>gt; » ou « igdb:    /// <summary>Clé : appid Steam.</summary>lt;numéro du jeu    /// <summary>Clé : appid Steam.</summary>gt; ».</summary>
    public Dictionary<string, StoreAboutCacheEntry> Apps { get; set; } = [];
}

/// <summary>
/// Présentation des jeux pour leur fiche (description, avis des joueurs, note de la presse, bandes-annonces), gardée 7 jours dans
/// store-about.json ; l'ancienne réponse sert encore si le service ne répond pas. Sources : magasin Steam (appid,
/// <see cref="SteamStoreAbout"/>), magasin GOG (identifiant du produit, <see cref="GogStoreAbout"/>), IGDB en recours (numéro
/// du jeu, identifiants de l'utilisateur, <see cref="IgdbAbout"/>). Seul ce numéro est envoyé, et seulement quand une fiche est
/// ouverte.
/// </summary>
public sealed class StoreAboutClient(AppPaths paths, FileLog log, TimeProvider time, Artwork.IgdbClient igdb)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly HttpClient Http = CreateClient();

    private readonly Lock _lock = new();
    private readonly JsonStateStore<StoreAboutCache> _store = new(Path.Combine(paths.Root, "store-about.json"));
    private StoreAboutCache? _cache;

    /// <summary>IGDB n'est interrogé qu'avec les identifiants de l'utilisateur.</summary>
    public bool CanUseIgdb => igdb.IsConfigured;

    /// <summary>Présentation Steam déjà en cache (même périmée), sans réseau ; null si jamais obtenue.</summary>
    public GameAbout? Cached(string appId) => Cached(AboutSource.Steam, appId);

    /// <summary>Présentation déjà en cache (même périmée), sans réseau ; null si jamais obtenue.</summary>
    public GameAbout? Cached(AboutSource source, string id) => Cache(Key(source, id)) is { } entry ? Parse(entry, source, id) : null;

    /// <summary>Présentation Steam à jour.</summary>
    public Task<GameAbout?> GetAsync(string appId, CancellationToken cancellation = default) => GetAsync(AboutSource.Steam, appId, cancellation);

    /// <summary>Présentation à jour (réseau si le cache a plus de 7 jours) ; null si la source n'a rien pour ce jeu.</summary>
    public async Task<GameAbout?> GetAsync(AboutSource source, string id, CancellationToken cancellation = default)
    {
        var key = Key(source, id);
        var cached = Cache(key);
        if (cached is not null && time.GetUtcNow() - cached.FetchedAt < MaxAge) return Parse(cached, source, id);
        if (source == AboutSource.Igdb && (!igdb.IsConfigured || !long.TryParse(id, out _))) return cached is null ? null : Parse(cached, source, id);

        var entry = new StoreAboutCacheEntry { FetchedAt = time.GetUtcNow() };
        try
        {
            switch (source)
            {
                case AboutSource.Steam:
                    entry.DetailsJson = await Http.GetStringAsync(SteamStoreAbout.DetailsUri(id), cancellation);
                    entry.ReviewsJson = await Http.GetStringAsync(SteamStoreAbout.ReviewsUri(id), cancellation);
                    break;
                case AboutSource.Gog:
                    entry.DetailsJson = await GetOrEmptyIfUnknownAsync(GogStoreAbout.ProductUri(id), cancellation);
                    entry.ReviewsJson = await Http.GetStringAsync(GogStoreAbout.RatingUri(id), cancellation);
                    break;
                case AboutSource.Igdb:
                    entry.DetailsJson = await igdb.AboutAsync(long.Parse(id), cancellation);
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or FormatException)
        {
            log.Warn($"Présentation {source} du jeu {id} indisponible ({ex.Message}){(cached is null ? "" : " : ancienne réponse utilisée")}.");
            return cached is null ? null : Parse(cached, source, id);
        }

        var result = Parse(entry, source, id);
        // Une réponse illisible n'écrase pas une ancienne réponse lisible (et reste au journal).
        if (result is not null || cached is null) Store(key, entry);
        return result ?? (cached is null ? null : Parse(cached, source, id));
    }

    /// <summary>Réponse du service, ou « » si le produit lui est inconnu (HTTP 404 : GOG) — mémorisé comme « rien ».</summary>
    private static async Task<string> GetOrEmptyIfUnknownAsync(Uri uri, CancellationToken cancellation)
    {
        try
        {
            return await Http.GetStringAsync(uri, cancellation);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return "";
        }
    }

    private static string Key(AboutSource source, string id) => source switch
    {
        AboutSource.Gog => "gog:" + id,
        AboutSource.Igdb => "igdb:" + id,
        _ => id, // Steam : l'appid seul (clés déjà écrites)
    };

    /// <summary>
    /// Vignette d'une bande-annonce, téléchargée une fois dans covers\trailers (*.steamstatic.com seulement, 2 Mo au plus) ;
    /// null si indisponible.
    /// </summary>
    public async Task<string?> ThumbnailAsync(string url, CancellationToken cancellation = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !SteamStoreAbout.IsSteamMedia(uri)) return null;
        var directory = Path.Combine(paths.Root, "covers", "trailers");
        var path = Path.Combine(directory, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..20] + ".jpg");
        if (File.Exists(path)) return path;
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxThumbnailBytes) throw new IOException("vignette trop grande");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
            if (bytes.Length > MaxThumbnailBytes) throw new IOException("vignette trop grande");
            Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellation);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            log.Warn($"Vignette de bande-annonce indisponible ({uri.Host}) : {ex.Message}");
            return null;
        }
    }

    private const long MaxThumbnailBytes = 2 * 1024 * 1024;

    private GameAbout? Parse(StoreAboutCacheEntry entry, AboutSource source, string id)
    {
        try
        {
            GameAbout? about;
            switch (source)
            {
                case AboutSource.Gog:
                    var (summary, details, videos) = entry.DetailsJson.Length > 0 ? GogStoreAbout.ParseProduct(entry.DetailsJson) : ("", [], []);
                    var rating = entry.ReviewsJson.Length > 0 ? GogStoreAbout.ParseRating(entry.ReviewsJson) : null;
                    about = new GameAbout(summary, details, null, null, [], AboutSource.Gog, rating, videos);
                    break;
                case AboutSource.Igdb:
                    about = entry.DetailsJson.Length > 0 ? IgdbAbout.Parse(entry.DetailsJson) : null;
                    break;
                default:
                    var steam = entry.DetailsJson.Length > 0 ? SteamStoreAbout.ParseDetails(entry.DetailsJson, id) : null;
                    var players = entry.ReviewsJson.Length > 0 ? SteamStoreAbout.ParseReviews(entry.ReviewsJson) : null;
                    about = new GameAbout(steam?.Summary ?? "", steam?.Details ?? [], steam?.Press, players, steam?.Trailers ?? []);
                    break;
            }
            return about is null || about.IsEmpty ? null : about;
        }
        catch (System.Text.Json.JsonException ex)
        {
            log.Error($"Présentation {source} illisible pour le jeu {id}", ex);
            return null;
        }
    }

    private StoreAboutCacheEntry? Cache(string key)
    {
        lock (_lock)
        {
            _cache ??= _store.Load() is { Version: StoreAboutCache.CurrentVersion } loaded ? loaded : new StoreAboutCache();
            return _cache.Apps.GetValueOrDefault(key);
        }
    }

    private void Store(string key, StoreAboutCacheEntry entry)
    {
        lock (_lock)
        {
            _cache!.Apps[key] = entry;
            _store.Save(_cache);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
