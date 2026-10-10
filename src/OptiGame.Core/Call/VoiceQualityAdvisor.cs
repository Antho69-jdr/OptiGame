namespace OptiGame.Core.Call;

/// <summary>Débit conseillé pour un micro, avec son explication.</summary>
public sealed record VoiceQualityAdvice(int Kbps, int? BandwidthHz, string Reason);

/// <summary>
/// Débit automatique de la voix : le plus élevé qui apporte encore quelque chose avec CE micro. Au-delà de ce que le micro capte,
/// plus de débit n'améliore rien (constaté par l'utilisateur le 2026-10-10 : 96 / 128 / 192 kbit/s indiscernables). Deux sources :
/// le format du micro dans Windows (fréquence d'échantillonnage : un casque Bluetooth en mode appel = 16 kHz, rien au-dessus de
/// 8 kHz) et la bande réellement captée de la voix, mesurée pendant qu'on parle (call.html : spectre des trames de voix comparé à
/// celui des silences). Paliers Opus mono : bande étroite (≤ 4 kHz) 32, large (≤ 8 kHz) 48, ≤ 12 kHz 64, ≤ 16 kHz 96, pleine
/// bande 128 kbit/s (192 n'apporte rien de mesurable pour une voix mono : reste au choix manuel).
/// </summary>
public static class VoiceQualityAdvisor
{
    /// <summary>Sans aucune information sur le micro : le débit « Haute ».</summary>
    public const int DefaultKbps = 128;

    /// <summary>Trames de voix (100 ms) nécessaires pour croire une mesure de bande : 5 s de parole.</summary>
    public const int MinVoiceFrames = 50;

    public static VoiceQualityAdvice Recommend(int? sampleRate, int? measuredBandwidthHz)
    {
        int? limit = sampleRate is > 0 ? sampleRate / 2 : null;
        int? bandwidth = (limit, measuredBandwidthHz) switch
        {
            ({ } l, { } m) => Math.Min(l, m),
            ({ } l, null) => l,
            (null, { } m) => m,
            _ => null,
        };
        // Un format large (48 kHz) ne dit rien de ce que le micro capte : seul un format étroit (casque Bluetooth à 16 kHz) décide sans mesure.
        if (bandwidth is null || (measuredBandwidthHz is null && limit >= 16_000))
        {
            return new(DefaultKbps, null, $"Débit automatique : {DefaultKbps} kbit/s, en attendant de mesurer ce micro (parlez quelques secondes en appel ou avec « Tester mon micro »).");
        }

        var kbps = bandwidth switch
        {
            <= 4_500 => 32,
            <= 8_500 => 48,
            <= 12_500 => 64,
            <= 16_500 => 96,
            _ => 128,
        };
        var khz = Math.Round(bandwidth.Value / 1000.0, bandwidth < 10_000 ? 1 : 0);
        var source = measuredBandwidthHz is null ? $"votre micro est réglé à {sampleRate / 1000.0:0.#} kHz dans Windows"
            : $"votre micro capte la voix jusqu'à {khz:0.#} kHz";
        var tail = kbps >= 128 ? "le débit haut en profite" : "plus de débit n'apporterait rien";
        return new(kbps, bandwidth, $"Débit automatique : {kbps} kbit/s. {char.ToUpperInvariant(source[0])}{source[1..]} : {tail}.");
    }

    /// <summary>Nom d'un micro sans le préfixe du moteur web pour le micro par défaut (« Par défaut - Microphone (Yeti Nano) »).</summary>
    public static string DeviceName(string label)
    {
        var dash = label.IndexOf(" - ", StringComparison.Ordinal);
        return dash is > 0 and < 20 ? label[(dash + 3)..] : label;
    }
}
