using OptiGame.Core;
using OptiGame.Core.Logging;
using OptiGame.Core.Rating;
using OptiGame.Core.State;

namespace OptiGame.Platform.Library;

public sealed class RequirementsCacheEntry
{
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Réponse brute de Steam (appdetails), relue à chaque fois : le format reste vérifiable.</summary>
    public string Json { get; set; } = "";
}

public sealed class RequirementsCache
{
    public int Version { get; set; } = 1;

    public Dictionary<string, RequirementsCacheEntry> Apps { get; set; } = [];
}

/// <summary>
/// Configurations requises des jeux Steam (service public appdetails du magasin Steam ; seul l'appid est envoyé).
/// Gardées 30 jours dans requirements.json ; l'ancienne réponse sert encore si Steam ne répond pas.
/// </summary>
public sealed class SteamRequirementsClient(AppPaths paths, FileLog log, TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly HttpClient Http = CreateClient();

    private readonly Lock _lock = new();
    private readonly JsonStateStore<RequirementsCache> _store = new(Path.Combine(paths.Root, "requirements.json"));
    private RequirementsCache? _cache;

    public async Task<SystemRequirements?> GetAsync(string appId, CancellationToken cancellation = default)
    {
        RequirementsCacheEntry? cached;
        lock (_lock)
        {
            _cache ??= _store.Load() ?? new RequirementsCache();
            cached = _cache.Apps.GetValueOrDefault(appId);
        }
        if (cached is not null && time.GetUtcNow() - cached.FetchedAt < MaxAge) return Parse(cached.Json, appId);

        try
        {
            var json = await Http.GetStringAsync(SteamRequirements.AppDetailsUri(appId), cancellation);
            var parsed = Parse(json, appId);
            lock (_lock)
            {
                _cache!.Apps[appId] = new RequirementsCacheEntry { FetchedAt = time.GetUtcNow(), Json = json };
                _store.Save(_cache);
            }
            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            log.Warn($"Configuration requise de l'appid {appId} indisponible ({ex.Message}){(cached is null ? "" : " : ancienne réponse utilisée")}.");
            return cached is null ? null : Parse(cached.Json, appId);
        }
    }

    private SystemRequirements? Parse(string json, string appId)
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

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
