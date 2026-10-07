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
    public int Version { get; set; } = 1;

    /// <summary>Clé : appid Steam.</summary>
    public Dictionary<string, StoreAboutCacheEntry> Apps { get; set; } = [];
}

/// <summary>
/// Présentation des jeux Steam pour leur fiche (description, avis des joueurs, note de la presse ; <see cref="SteamStoreAbout"/>),
/// gardée 7 jours dans store-about.json ; l'ancienne réponse sert encore si Steam ne répond pas. Seul l'appid est envoyé, et
/// seulement quand une fiche est ouverte.
/// </summary>
public sealed class StoreAboutClient(AppPaths paths, FileLog log, TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly HttpClient Http = CreateClient();

    private readonly Lock _lock = new();
    private readonly JsonStateStore<StoreAboutCache> _store = new(Path.Combine(paths.Root, "store-about.json"));
    private StoreAboutCache? _cache;

    /// <summary>Présentation déjà en cache (même périmée), sans réseau ; null si jamais obtenue.</summary>
    public GameAbout? Cached(string appId) => Cache(appId) is { } entry ? Parse(entry, appId) : null;

    /// <summary>Présentation à jour (réseau si le cache a plus de 7 jours) ; null si Steam n'a rien pour ce jeu.</summary>
    public async Task<GameAbout?> GetAsync(string appId, CancellationToken cancellation = default)
    {
        var cached = Cache(appId);
        if (cached is not null && time.GetUtcNow() - cached.FetchedAt < MaxAge) return Parse(cached, appId);

        var entry = new StoreAboutCacheEntry { FetchedAt = time.GetUtcNow() };
        try
        {
            entry.DetailsJson = await Http.GetStringAsync(SteamStoreAbout.DetailsUri(appId), cancellation);
            entry.ReviewsJson = await Http.GetStringAsync(SteamStoreAbout.ReviewsUri(appId), cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            log.Warn($"Présentation Steam de l'appid {appId} indisponible ({ex.Message}){(cached is null ? "" : " : ancienne réponse utilisée")}.");
            return cached is null ? null : Parse(cached, appId);
        }

        var about = Parse(entry, appId);
        // Une réponse illisible n'écrase pas une ancienne réponse lisible (et reste au journal).
        if (about is not null || cached is null) Store(appId, entry);
        return about ?? (cached is null ? null : Parse(cached, appId));
    }

    private GameAbout? Parse(StoreAboutCacheEntry entry, string appId)
    {
        try
        {
            var details = entry.DetailsJson.Length > 0 ? SteamStoreAbout.ParseDetails(entry.DetailsJson, appId) : null;
            var players = entry.ReviewsJson.Length > 0 ? SteamStoreAbout.ParseReviews(entry.ReviewsJson) : null;
            var about = new GameAbout(details?.Summary ?? "", details?.Details ?? [], details?.Press, players);
            return about.IsEmpty ? null : about;
        }
        catch (System.Text.Json.JsonException ex)
        {
            log.Error($"Présentation Steam illisible pour l'appid {appId}", ex);
            return null;
        }
    }

    private StoreAboutCacheEntry? Cache(string appId)
    {
        lock (_lock)
        {
            _cache ??= _store.Load() ?? new StoreAboutCache();
            return _cache.Apps.GetValueOrDefault(appId);
        }
    }

    private void Store(string appId, StoreAboutCacheEntry entry)
    {
        lock (_lock)
        {
            _cache!.Apps[appId] = entry;
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
