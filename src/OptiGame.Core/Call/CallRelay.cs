namespace OptiGame.Core.Call;

/// <summary>
/// Serveur de mise en relation des appels (server/call-relay) : les deux PC s'y retrouvent par le code, s'échangent leurs
/// descriptions de connexion (adresses comprises), puis le quittent ; la voix ne passe jamais par lui. Seules des adresses sûres
/// sont acceptées : wss sur workers.dev, ou ws sur cette machine (tests).
/// </summary>
public static class CallRelay
{
    /// <summary>Adresse du serveur déployé ; vide tant qu'il ne l'est pas (l'appel le dit au lieu d'essayer).</summary>
    public const string DefaultUrl = "";

    /// <summary>Variable d'environnement qui remplace <see cref="DefaultUrl"/> (serveur de test local, autre déploiement).</summary>
    public const string OverrideVariable = "OPTIGAME_CALL_RELAY";

    /// <summary>Adresse utilisable ; null si absente ou refusée.</summary>
    public static Uri? Resolve(string? overrideUrl)
    {
        var text = string.IsNullOrWhiteSpace(overrideUrl) ? DefaultUrl : overrideUrl.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        return IsAllowed(uri) ? uri : null;
    }

    public static bool IsAllowed(Uri uri) =>
        (uri.Scheme == "wss" && uri.Host.EndsWith(".workers.dev", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort) ||
        (uri.Scheme == "ws" && uri.Host == "localhost");

    /// <summary>Adresse du salon d'un code, pour l'hôte ou l'invité.</summary>
    public static Uri Room(Uri relay, string code, bool host) =>
        new($"{relay.GetLeftPart(UriPartial.Authority)}/v1/rooms/{code}?role={(host ? "host" : "guest")}");
}
