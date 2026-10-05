using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptiGame.Core.Artwork;
using OptiGame.Core.Logging;
using OptiGame.Core.Settings;
using OptiGame.Core.Text;

namespace OptiGame.Platform.Artwork;

/// <summary>Chiffrement du secret Twitch avec DPAPI (lisible uniquement par ce compte Windows sur ce PC).</summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = "OptiGame.Igdb"u8.ToArray();

    public static string Protect(string secret) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // Autre compte / autre PC : il faudra ressaisir le secret.
        }
    }
}

/// <summary>
/// Client IGDB : jeton Twitch « client_credentials » (gardé en mémoire jusqu'à son expiration) et recherche de jeux.
/// Respecte la limite de 4 requêtes par seconde (une requête à la fois, espacées d'au moins 300 ms).
/// Seul le nom du jeu est envoyé.
/// </summary>
public sealed class IgdbClient(AppSettingsStore settings, FileLog log)
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(300);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private string? _token;
    private DateTimeOffset _tokenExpires;
    private string? _tokenClientId;

    public bool IsConfigured
    {
        get
        {
            var s = settings.Get();
            return !string.IsNullOrWhiteSpace(s.IgdbClientId) && !string.IsNullOrEmpty(s.IgdbClientSecretProtected);
        }
    }

    public async Task<IReadOnlyList<IgdbGame>> SearchAsync(string name, CancellationToken cancellation = default)
    {
        var json = await PostGamesAsync(Igdb.SearchQuery(name), cancellation);
        return Igdb.ParseGames(json);
    }

    /// <summary>Vérifie les identifiants (obtention d'un jeton et une petite recherche).</summary>
    public async Task<string> TestAsync(CancellationToken cancellation = default)
    {
        _token = null;
        var results = await SearchAsync("Overwatch", cancellation);
        return $"Connexion réussie : {FrenchText.Count(results.Count, "résultat", "résultats")} pour « Overwatch ».";
    }

    private async Task<string> PostGamesAsync(string body, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var (clientId, token) = await GetTokenAsync(cancellation);
                await ThrottleAsync(cancellation);

                using var request = new HttpRequestMessage(HttpMethod.Post, Igdb.GamesEndpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/plain"),
                };
                request.Headers.Add("Client-ID", clientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await Http.SendAsync(request, cancellation);
                var content = await response.Content.ReadAsStringAsync(cancellation);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _token = null; // Jeton expiré ou révoqué : on en redemande un, une fois.
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    log.Error($"IGDB /games : HTTP {(int)response.StatusCode} — {Excerpt(content)}");
                    throw new HttpRequestException($"IGDB a répondu {(int)response.StatusCode} {response.ReasonPhrase}.");
                }
                return content;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string ClientId, string Token)> GetTokenAsync(CancellationToken cancellation)
    {
        var s = settings.Get();
        var clientId = s.IgdbClientId?.Trim();
        var secret = SecretProtector.Unprotect(s.IgdbClientSecretProtected);
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException("Identifiants IGDB manquants : renseignez-les dans Paramètres.");
        }

        if (_token is not null && _tokenClientId == clientId && DateTimeOffset.UtcNow < _tokenExpires)
        {
            return (clientId, _token);
        }

        var url = $"{Igdb.TokenEndpoint}?client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(secret)}&grant_type=client_credentials";
        using var response = await Http.PostAsync(url, content: null, cancellation);
        var json = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            // Le corps ne contient pas le secret (il est dans l'URL, jamais journalisée).
            log.Error($"Jeton Twitch : HTTP {(int)response.StatusCode} — {Excerpt(json)}");
            throw new HttpRequestException(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden
                ? "Identifiants Twitch refusés : vérifiez le Client ID et le Client Secret."
                : $"Twitch a répondu {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        using var document = JsonDocument.Parse(json);
        _token = document.RootElement.GetProperty("access_token").GetString()
            ?? throw new FormatException("Réponse Twitch sans access_token.");
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var s2) ? s2 : 3600;
        _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
        _tokenClientId = clientId;
        log.Info($"Jeton IGDB obtenu (valide {expiresIn / 86400} jour(s)).");
        return (clientId, _token);
    }

    private async Task ThrottleAsync(CancellationToken cancellation)
    {
        var wait = _lastRequest + MinInterval - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellation);
        _lastRequest = DateTimeOffset.UtcNow;
    }

    private static string Excerpt(string text) => text.Length <= 300 ? text : text[..300] + "…";

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiGame/1.0");
        return client;
    }
}
