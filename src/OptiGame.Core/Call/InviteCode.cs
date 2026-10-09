using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace OptiGame.Core.Call;

/// <summary>Sens d'un code d'appel : invitation (créée par l'hôte) ou réponse (renvoyée par l'invité).</summary>
public enum InviteKind
{
    Invitation,
    Answer,
}

/// <summary>
/// Code échangé à la main pour ouvrir un appel pair à pair (aucun serveur d'OptiGame) : la description de connexion WebRTC
/// (SDP, avec ses adresses) compressée, plus une date limite. « OG1I… » = invitation, « OG1R… » = réponse. Le code contient
/// les adresses réseau de son auteur : ne l'envoyer qu'à la personne invitée (dit à l'écran).
/// </summary>
public static class InviteCode
{
    /// <summary>Durée de validité d'un code : au-delà, l'invitation est refusée (un code intercepté plus tard ne sert à rien).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>Taille maximale d'un code accepté (un vrai code fait moins de 1 500 caractères).</summary>
    public const int MaxLength = 8000;

    /// <summary>Taille maximale d'une description de connexion décompressée.</summary>
    private const int MaxSdpLength = 20000;

    private const string Prefix = "OG1";

    private sealed record Payload(string Sdp, long Expires);

    public static string Create(InviteKind kind, string sdp, DateTimeOffset now)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Payload(sdp, (now + Lifetime).ToUnixTimeSeconds()));
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true)) deflate.Write(json);
        return Prefix + (kind == InviteKind.Invitation ? "I" : "R") + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Lit un code collé (espaces et retours à la ligne ignorés). FormatException, message en français : pas un code d'appel,
    /// mauvais sens (une invitation là où une réponse est attendue), code expiré ou abîmé.
    /// </summary>
    public static string Read(string code, InviteKind expected, DateTimeOffset now)
    {
        var text = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (text.Length > MaxLength) throw new FormatException("Ce code est trop long : ce n'est pas un code d'appel.");
        if (!text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length < 5)
        {
            throw new FormatException("Ce n'est pas un code d'appel d'OptiGame (il commence par « OG1 »).");
        }
        var kind = text[3] switch { 'I' => InviteKind.Invitation, 'R' => InviteKind.Answer, _ => throw new FormatException("Ce code d'appel est abîmé.") };
        if (kind != expected)
        {
            throw new FormatException(expected == InviteKind.Answer
                ? "C'est un code d'invitation : il faut la RÉPONSE de votre ami (le code qu'il a obtenu en collant le vôtre)."
                : "C'est un code de réponse : pour rejoindre, collez l'INVITATION de votre ami.");
        }

        Payload? payload;
        try
        {
            var base64 = text[4..].Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            using var input = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = input.Read(chunk)) > 0)
            {
                output.Write(chunk, 0, read);
                if (output.Length > MaxSdpLength * 2) throw new FormatException("Ce code d'appel est abîmé.");
            }
            payload = JsonSerializer.Deserialize<Payload>(output.ToArray());
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException)
        {
            throw new FormatException("Ce code d'appel est abîmé (copié en partie ?).", ex);
        }
        if (payload is null || payload.Sdp.Length is 0 or > MaxSdpLength || !payload.Sdp.StartsWith("v=0", StringComparison.Ordinal))
        {
            throw new FormatException("Ce code d'appel est abîmé.");
        }
        if (DateTimeOffset.FromUnixTimeSeconds(payload.Expires) < now)
        {
            throw new FormatException("Ce code a expiré (valable 15 minutes) : demandez-en un nouveau.");
        }
        if (DateTimeOffset.FromUnixTimeSeconds(payload.Expires) > now + Lifetime + TimeSpan.FromMinutes(5))
        {
            throw new FormatException("Ce code a une date impossible (horloge d'un des deux PC déréglée ?).");
        }
        return payload.Sdp;
    }

    /// <summary>Empreinte DTLS d'une description de connexion (« a=fingerprint:sha-256 AB:CD:… ») ; null si absente.</summary>
    public static string? Fingerprint(string sdp) =>
        sdp.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase))?["a=fingerprint:".Length..].Trim();
}
