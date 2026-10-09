using System.Security.Cryptography;

namespace OptiGame.Core.Call;

/// <summary>
/// Code d'un appel vocal : 6 caractères tirés au hasard (« OG-K7P2Q9 » à l'écran), numéro du salon de mise en relation où les deux PC
/// se retrouvent (<see cref="CallRelay"/>). Valable <see cref="Lifetime"/> : au-delà, le serveur ferme le salon. Alphabet sans
/// caractères qu'on confond à l'oral ou à la lecture (0/O, 1/I/L) : 31^6 ≈ 887 millions de codes.
/// </summary>
public static class CallCode
{
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public const int Length = 6;

    /// <summary>Préfixe affiché (et accepté au collage) devant le code.</summary>
    public const string Prefix = "OG-";

    /// <summary>Durée pendant laquelle l'ami peut rejoindre (fixée par le serveur, rappelée par le compte à rebours).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public static string Create()
    {
        Span<char> code = stackalloc char[Length];
        for (var i = 0; i < Length; i++) code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(code);
    }

    /// <summary>« OG-K7P2Q9 ».</summary>
    public static string Display(string code) => Prefix + code;

    /// <summary>
    /// Code saisi ou collé → 6 caractères ; espaces, tirets, minuscules et préfixe « OG » acceptés. FormatException (message en
    /// français) si ce n'est pas un code d'appel.
    /// </summary>
    public static string Parse(string text)
    {
        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (cleaned.Length == Length + 2 && cleaned.StartsWith("OG", StringComparison.Ordinal)) cleaned = cleaned[2..];
        if (cleaned.Length != Length)
        {
            throw new FormatException($"Un code d'appel a {Length} caractères après « {Prefix} », par exemple « {Prefix}K7P2Q9 ».");
        }
        if (cleaned.Any(c => !Alphabet.Contains(c)))
        {
            throw new FormatException("Ce code contient un caractère qu'OptiGame n'utilise pas (ni 0, ni O, ni 1, ni I, ni L) : vérifiez-le.");
        }
        return cleaned;
    }

    /// <summary>Empreinte de chiffrement (DTLS) d'une description de connexion : « sha-256 AB:CD:… » ; null si absente.</summary>
    public static string? Fingerprint(string sdp) =>
        sdp.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase))?["a=fingerprint:".Length..].Trim();
}
