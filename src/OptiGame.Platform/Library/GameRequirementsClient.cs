using OptiGame.Core;
using OptiGame.Core.Logging;
using OptiGame.Core.Rating;
using OptiGame.Core.State;

namespace OptiGame.Platform.Library;

public sealed class RequirementsCacheEntry
{
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Réponse brute (Steam appdetails ou page PCGamingWiki), relue à chaque fois : le format reste vérifiable.
    /// Vide = jeu introuvable sur PCGamingWiki (pour ne pas le rechercher à chaque ouverture).</summary>
    public string Json { get; set; } = "";

    /// <summary>Titre de la page PCGamingWiki (null pour Steam).</summary>
    public string? Title { get; set; }
}

public sealed class RequirementsCache
{
    public int Version { get; set; } = 1;

    /// <summary>Clé : appid Steam, ou « pcgw:&lt;nom simplifié&gt; » pour PCGamingWiki.</summary>
    public Dictionary<string, RequirementsCacheEntry> Apps { get; set; } = [];
}

/// <summary>
/// Configurations requises des jeux, gardées 30 jours dans requirements.json (l'ancienne réponse sert encore si le service ne
/// répond pas) :
/// <list type="bullet">
/// <item>Steam (service public appdetails du magasin) : seul l'appid est envoyé ;</item>
/// <item>PCGamingWiki pour les autres jeux (<see cref="PcGamingWiki"/>) : seul le nom du jeu est envoyé ; un jeu introuvable
/// n'est recherché à nouveau qu'au bout de 7 jours.</item>
/// </list>
/// </summary>
public sealed class GameRequirementsClient(AppPaths paths, FileLog log, TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan NotFoundMaxAge = TimeSpan.FromDays(7);
    private static readonly HttpClient Http = CreateClient();

    private readonly Lock _lock = new();
    private readonly JsonStateStore<RequirementsCache> _store = new(Path.Combine(paths.Root, "requirements.json"));
    private RequirementsCache? _cache;

    /// <summary>Configuration requise publiée sur Steam.</summary>
    public async Task<SystemRequirements?> GetAsync(string appId, CancellationToken cancellation = default)
    {
        var cached = Cached(appId);
        if (cached is not null && time.GetUtcNow() - cached.FetchedAt < MaxAge) return ParseSteam(cached.Json, appId);

        try
        {
            var json = await Http.GetStringAsync(SteamRequirements.AppDetailsUri(appId), cancellation);
            var parsed = ParseSteam(json, appId);
            Store(appId, new RequirementsCacheEntry { FetchedAt = time.GetUtcNow(), Json = json });
            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            log.Warn($"Configuration requise de l'appid {appId} indisponible ({ex.Message}){(cached is null ? "" : " : ancienne réponse utilisée")}.");
            return cached is null ? null : ParseSteam(cached.Json, appId);
        }
    }

    /// <summary>Configuration requise trouvée sur PCGamingWiki d'après le nom du jeu (titre identique seulement).</summary>
    public async Task<SystemRequirements?> FindByNameAsync(string gameName, CancellationToken cancellation = default)
    {
        var key = "pcgw:" + PcGamingWiki.Key(gameName);
        if (key == "pcgw:") return null;
        var cached = Cached(key);
        if (cached is not null && time.GetUtcNow() - cached.FetchedAt < (cached.Title is null ? NotFoundMaxAge : MaxAge))
        {
            return cached.Title is null ? null : ParseWiki(cached.Json, cached.Title);
        }

        try
        {
            var title = PcGamingWiki.PickTitle(await Http.GetStringAsync(PcGamingWiki.SearchUri(gameName), cancellation), gameName);
            if (title is null)
            {
                log.Info($"PCGamingWiki : aucune page intitulée « {gameName} » ; nouvel essai dans 7 jours.");
                Store(key, new RequirementsCacheEntry { FetchedAt = time.GetUtcNow() });
                return null;
            }
            var json = await Http.GetStringAsync(PcGamingWiki.PageUri(title), cancellation);
            var parsed = ParseWiki(json, title);
            Store(key, new RequirementsCacheEntry { FetchedAt = time.GetUtcNow(), Json = json, Title = title });
            log.Info($"PCGamingWiki : configuration requise de « {gameName} » {(parsed is null ? "absente de" : "lue sur")} la page « {title} ».");
            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
        {
            log.Warn($"PCGamingWiki indisponible pour « {gameName} » ({ex.Message}){(cached?.Title is null ? "" : " : ancienne réponse utilisée")}.");
            return cached?.Title is null ? null : ParseWiki(cached.Json, cached.Title);
        }
    }

    private RequirementsCacheEntry? Cached(string key)
    {
        lock (_lock)
        {
            _cache ??= _store.Load() ?? new RequirementsCache();
            return _cache.Apps.GetValueOrDefault(key);
        }
    }

    private void Store(string key, RequirementsCacheEntry entry)
    {
        lock (_lock)
        {
            _cache!.Apps[key] = entry;
            _store.Save(_cache);
        }
    }

    private SystemRequirements? ParseSteam(string json, string appId)
    {
        try
        {
            return SteamRequirements.ParseAppDetails(json, appId);
        }
        catch (System.Text.Json.JsonException ex)
        {
            log.Error($"Réponse de Steam illisible pour l'appid {appId}", ex);
            return null;
        }
    }

    private SystemRequirements? ParseWiki(string json, string title)
    {
        try
        {
            return PcGamingWiki.ParsePage(json, title);
        }
        catch (System.Text.Json.JsonException ex)
        {
            log.Error($"Réponse de PCGamingWiki illisible pour « {title} »", ex);
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
