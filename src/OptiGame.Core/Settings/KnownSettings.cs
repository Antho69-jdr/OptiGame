using OptiGame.Core.State;

namespace OptiGame.Core.Settings;

/// <summary>
/// Réglages connus. Chaque chemin a été vérifié sur Windows 11 25H2 (build 26200) ; voir CLAUDE.md.
/// </summary>
public static class KnownSettings
{
    public const string RegistryKind = "registry";
    public const string PowerSchemeKind = "power-scheme";
    public const string DisplayModeKind = "display-mode";

    /// <summary>Mode Jeu. Absente = activé (défaut Windows).</summary>
    public static readonly SettingTarget GameMode =
        new(RegistryKind, @"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled");

    /// <summary>Enregistrement en arrière-plan (« Enregistrer ce qui s'est passé »). Absente = désactivé.</summary>
    public static readonly SettingTarget BackgroundRecording =
        new(RegistryKind, @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled");

    public static readonly SettingTarget AppCapture =
        new(RegistryKind, @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");

    /// <summary>Interrupteur global des captures de jeu.</summary>
    public static readonly SettingTarget GameDvrEnabled =
        new(RegistryKind, @"HKCU\System\GameConfigStore", "GameDVR_Enabled");

    /// <summary>Stratégie de groupe (souvent posée par des outils d'optimisation). 0 = captures interdites.</summary>
    public static readonly SettingTarget GameDvrPolicy =
        new(RegistryKind, @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");

    /// <summary>HAGS : 2 = activé, 1 = désactivé, absente = choix du pilote. Pris en compte au redémarrage.</summary>
    public static readonly SettingTarget HwSchMode =
        new(RegistryKind, @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");

    private const string HvciKey = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    /// <summary>Intégrité de la mémoire : 1 = activée, 0 = désactivée. Pris en compte au redémarrage.</summary>
    public static readonly SettingTarget HvciEnabled = new(RegistryKind, HvciKey, "Enabled");

    /// <summary>1 = verrouillée (UEFI) : le registre ne suffit pas à la désactiver.</summary>
    public static readonly SettingTarget HvciLocked = new(RegistryKind, HvciKey, "Locked");

    /// <summary>Valeurs « Clé=Valeur; » par chemin d'exe. GpuPreference : 0 = Windows décide, 1 = économie, 2 = performance.</summary>
    public const string GpuPreferencesKey = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";

    public const string GpuPreferencesGlobalValue = "DirectXUserGlobalSettings";

    public static SettingTarget GpuPreference(string exePath) => new(RegistryKind, GpuPreferencesKey, exePath);

    /// <summary>Plan d'alimentation actif (valeur = GUID en texte).</summary>
    public static readonly SettingTarget ActivePowerScheme = new(PowerSchemeKind, "active");

    /// <summary>Mode d'affichage d'un écran (valeur = « largeur x hauteur @ Hz »).</summary>
    public static SettingTarget DisplayMode(string deviceName) => new(DisplayModeKind, deviceName);
}

public static class PowerSchemes
{
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
}
