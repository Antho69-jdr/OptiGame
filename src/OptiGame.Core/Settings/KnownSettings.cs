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
    public const string ProcessKind = "process";
    public const string PowerSettingKind = "power-setting";
    public const string NvidiaProfileKind = "nvidia-profile";
    public const string IniValueKind = "ini-value";

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

    /// <summary>Réglages graphiques globaux (« Clé=Valeur; ») ; SwapEffectUpgradeEnable = « Optimisations pour les jeux en mode fenêtré ».</summary>
    public static readonly SettingTarget DirectXGlobalSettings = new(RegistryKind, GpuPreferencesKey, GpuPreferencesGlobalValue);

    /// <summary>Plafond de FPS du pilote NVIDIA (FRL_FPS, 0x10835002) dans le profil que le pilote applique à cet exe ; DWord, Absent = non défini.</summary>
    public static SettingTarget NvidiaFrameRateLimit(string exePath) => new(NvidiaProfileKind, exePath, "0x10835002");

    /// <summary>Valeur d'un fichier .ini d'un jeu (GameUserSettings.ini d'Unreal Engine) ; Absent = clé absente. Path = « fichier|section ».</summary>
    public static SettingTarget IniValue(string file, string section, string key) => new(IniValueKind, $"{file}|{section}", key);

    /// <summary>Plan d'alimentation actif (valeur = GUID en texte).</summary>
    public static readonly SettingTarget ActivePowerScheme = new(PowerSchemeKind, "active");

    /// <summary>Réglage d'un plan d'alimentation, sur secteur (valeur = DWord). Path = « plan/sous-groupe/réglage ».</summary>
    public static SettingTarget PowerSetting(Guid scheme, Guid subgroup, Guid setting) =>
        new(PowerSettingKind, $"{scheme}/{subgroup}/{setting}", "ac");

    /// <summary>Mode d'affichage d'un écran (valeur = « largeur x hauteur @ Hz »).</summary>
    public static SettingTarget DisplayMode(string deviceName) => new(DisplayModeKind, deviceName);

    /// <summary>
    /// Programme en cours d'exécution dans la session. Valeur = ligne de commande pour le relancer, ou Absent
    /// s'il ne tourne pas. Écrire Absent le ferme ; écrire une ligne de commande le relance (non élevé) s'il ne tourne pas.
    /// </summary>
    public static SettingTarget RunningProcess(string exeName) => new(ProcessKind, exeName.ToLowerInvariant());
}

public static class PowerSchemes
{
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
}

/// <summary>Réglages des plans d'alimentation (GUID vérifiés avec powercfg /qh sur la machine de dev le 2026-10-02).</summary>
public static class PowerSettings
{
    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");

    /// <summary>PERFBOOSTMODE : 0 = désactivé, 1 = activé, 2 = offensif (défaut de Windows sur secteur), 3-6 = variantes.</summary>
    public static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

    /// <summary>PROCTHROTTLEMAX : état maximal du processeur, en %. Moins de 100 % bloque le boost.</summary>
    public static readonly Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
}
