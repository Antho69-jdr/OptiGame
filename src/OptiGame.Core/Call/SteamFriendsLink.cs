using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OptiGame.Core.Call;

/// <summary>
/// Amis Steam des appels : connexion avec Steam (OpenID, dans le navigateur, par le serveur de mise en relation) et identifiants.
/// Le serveur remet un jeton signé (« &lt;steamid&gt;.&lt;expiration ms&gt;.&lt;signature&gt; », 30 jours), gardé chiffré dans settings.json.
/// </summary>
public static partial class SteamFriendsLink
{
    /// <summary>Écart entre un SteamID64 et le numéro de compte (accountid) des fichiers de Steam.</summary>
    public const ulong AccountIdBase = 76561197960265728;

    [GeneratedRegex(@"^7656119\d{10}$")]
    private static partial Regex SteamIdPattern();

    public static bool IsSteamId(string? id) => id is not null && SteamIdPattern().IsMatch(id);

    public static string FromAccountId(uint accountId) => (AccountIdBase + accountId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Jeton de la demande de connexion (128 bits au hasard, base64url) : seul le PC qui l'a tiré reçoit le résultat.</summary>
    public static string NewLoginState() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Adresse https du serveur (pour le navigateur) à partir de son adresse wss (ws → http en test local).</summary>
    public static Uri HttpOrigin(Uri relay) =>
        new($"{(relay.Scheme == "wss" ? "https" : "http")}://{relay.Authority}");

    /// <summary>Page à ouvrir dans le navigateur : elle mène à la connexion de Steam.</summary>
    public static Uri LoginPage(Uri relay, string state) => new(HttpOrigin(relay), $"/v1/auth/steam?state={state}");

    /// <summary>WebSocket où le jeton arrive une fois la connexion vérifiée.</summary>
    public static Uri LoginWait(Uri relay, string state) => new($"{relay.GetLeftPart(UriPartial.Authority)}/v1/auth/wait?state={state}");

    public static Uri Presence(Uri relay) => new($"{relay.GetLeftPart(UriPartial.Authority)}/v1/presence");

    /// <summary>SteamID64 et date d'expiration lus dans un jeton (sans vérifier la signature : c'est le serveur qui la vérifie).</summary>
    public static (string SteamId, DateTimeOffset Expires)? ReadToken(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 } || !IsSteamId(parts[0]) || !long.TryParse(parts[1], out var expires)) return null;
        return (parts[0], DateTimeOffset.FromUnixTimeMilliseconds(expires));
    }
}
