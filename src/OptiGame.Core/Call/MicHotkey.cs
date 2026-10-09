using System.Globalization;

namespace OptiGame.Core.Call;

/// <summary>Effet du raccourci du micro pendant un appel.</summary>
public enum MicHotkeyMode
{
    /// <summary>Une pression coupe le micro, la suivante l'ouvre (son de confirmation).</summary>
    Toggle,
    /// <summary>Micro ouvert tant que la touche est enfoncée.</summary>
    PushToTalk,
}

/// <summary>
/// Raccourci du micro : une touche (code de touche virtuelle de Windows) et les modificateurs à tenir avec elle. Lu au clavier
/// pendant les appels seulement (Platform/Input/RawKeyboardListener). Gardé dans settings.json sous forme de texte
/// (« ctrl+alt+0x4D »).
/// </summary>
public sealed record MicHotkey(int VirtualKey, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    public const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

    /// <summary>Modificateurs et touches Windows : jamais la touche principale d'un raccourci.</summary>
    public static bool IsModifier(int vk) => vk is VkShift or VkControl or VkMenu or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    /// <summary>Touches gauche / droite ramenées au modificateur commun.</summary>
    public static int Normalize(int vk) => vk switch
    {
        0xA0 or 0xA1 => VkShift,
        0xA2 or 0xA3 => VkControl,
        0xA4 or 0xA5 => VkMenu,
        _ => vk,
    };

    /// <summary>« Ctrl + Alt + M » ; <paramref name="keyName"/> nomme les touches propres à la disposition du clavier (« ù », « ^ »).</summary>
    public string Label(Func<int, string?>? keyName = null)
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Maj");
        parts.Add(KeyLabel(VirtualKey, keyName));
        return string.Join(" + ", parts);
    }

    public static string KeyLabel(int vk, Func<int, string?>? keyName = null) => vk switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x60 and <= 0x69 => $"Pavé num. {vk - 0x60}",
        0x6A => "Pavé num. *",
        0x6B => "Pavé num. +",
        0x6D => "Pavé num. -",
        0x6E => "Pavé num. .",
        0x6F => "Pavé num. /",
        0x20 => "Espace",
        0x09 => "Tab",
        0x0D => "Entrée",
        0x08 => "Retour arrière",
        0x2D => "Inser",
        0x2E => "Suppr",
        0x24 => "Début",
        0x23 => "Fin",
        0x21 => "Page préc.",
        0x22 => "Page suiv.",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x13 => "Pause",
        0x91 => "Arrêt défil.",
        0x14 => "Verr. maj.",
        0x90 => "Verr. num.",
        0x2C => "Impr. écran",
        0x5D => "Menu",
        _ => keyName?.Invoke(vk) is { Length: > 0 } name ? name : $"Touche {vk:X2}",
    };

    public string Serialize() =>
        string.Concat(Ctrl ? "ctrl+" : "", Alt ? "alt+" : "", Shift ? "shift+" : "", $"0x{VirtualKey:X2}");

    /// <summary>Texte de settings.json → raccourci ; null si vide ou illisible (raccourci retiré plutôt que deviné).</summary>
    public static MicHotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var key = parts[^1];
        if (!key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(key[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vk)
            || vk is <= 0 or >= 0xFF || IsModifier(vk) || vk == 0x1B)
        {
            return null;
        }
        var modifiers = parts[..^1].Select(p => p.ToLowerInvariant()).ToList();
        if (modifiers.Any(m => m is not ("ctrl" or "alt" or "shift"))) return null;
        return new MicHotkey(vk, modifiers.Contains("ctrl"), modifiers.Contains("alt"), modifiers.Contains("shift"));
    }
}

/// <summary>
/// Suit les touches enfoncées et dit quand le raccourci est pressé (+1) ou relâché (−1). Les modificateurs demandés doivent être
/// tenus ; d'autres touches tenues en plus n'empêchent rien (courir avec Maj en parlant). La répétition automatique est ignorée.
/// </summary>
public sealed class MicHotkeyMatcher(MicHotkey hotkey)
{
    private readonly HashSet<int> _down = [];
    private bool _active;

    public MicHotkey Hotkey { get; } = hotkey;

    public int OnKey(int virtualKey, bool isDown)
    {
        var vk = MicHotkey.Normalize(virtualKey);
        if (isDown)
        {
            if (!_down.Add(vk)) return 0; // répétition automatique
            if (vk != Hotkey.VirtualKey || _active) return 0;
            if (Hotkey.Ctrl && !_down.Contains(MicHotkey.VkControl)) return 0;
            if (Hotkey.Alt && !_down.Contains(MicHotkey.VkMenu)) return 0;
            if (Hotkey.Shift && !_down.Contains(MicHotkey.VkShift)) return 0;
            _active = true;
            return 1;
        }
        _down.Remove(vk);
        if (!_active || vk != Hotkey.VirtualKey) return 0;
        _active = false;
        return -1;
    }
}

/// <summary>Suppression du bruit de fond de la voix pendant les appels.</summary>
public enum NoiseSuppression
{
    /// <summary>Filtre RNNoise (réseau de neurones spécialisé dans la voix), sur ce PC : clavier, ventilateur, bruits de fond.</summary>
    Strong,
    /// <summary>Suppression intégrée au moteur web (plus légère, moins efficace).</summary>
    Standard,
    /// <summary>Aucune (micro de studio, casque à réduction de bruit).</summary>
    Off,
}

/// <summary>Débit de la voix envoyée (Opus mono) : chacun choisit ce qu'il envoie.</summary>
public enum VoiceQuality
{
    Standard = 96,
    High = 128,
    Max = 192,
}

/// <summary>Seuil du micro (« noise gate ») : le son ne passe qu'au-dessus d'un niveau (voix de fond coupées).</summary>
public enum MicGateMode
{
    /// <summary>Seuil appris pendant l'appel, entre le fond sonore et votre voix.</summary>
    Auto,
    /// <summary>Seuil choisi (dBFS).</summary>
    Manual,
    Off,
}
