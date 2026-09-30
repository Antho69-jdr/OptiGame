namespace OptiGame.Core.Abstractions;

// Sources de données en lecture seule. Les implémentations réelles sont dans OptiGame.Platform ;
// Core ne contient que la logique de décision, testable avec des fakes.

public sealed record DisplayInfo(
    string DeviceName,
    string MonitorName,
    int Width,
    int Height,
    int CurrentHz,
    int MaxHzAtCurrentResolution);

public interface IDisplayInfoProvider
{
    IReadOnlyList<DisplayInfo> GetDisplays();
}

public sealed record MemoryModule(
    string BankLabel,
    string DeviceLocator,
    ulong CapacityBytes,
    uint? SpeedMts,
    uint? ConfiguredSpeedMts,
    string? PartNumber,
    uint SmbiosMemoryType);

public sealed record MemoryInfo(IReadOnlyList<MemoryModule> Modules, int? TotalSlots);

public interface IMemoryInfoProvider
{
    MemoryInfo GetMemoryInfo();
}

public sealed record GpuAdapter(string Name, string? DriverVersion, DateTime? DriverDate, string PnpDeviceId)
{
    /// <summary>GPU matériel (PCI), par opposition aux adaptateurs virtuels ou « Microsoft Basic Display ».</summary>
    public bool IsPhysical => PnpDeviceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase);
}

public interface IGpuInfoProvider
{
    IReadOnlyList<GpuAdapter> GetAdapters();
}

public sealed record GpuSchedulingState(string AdapterName, bool Supported, bool Enabled);

public interface IGpuSchedulingProvider
{
    IReadOnlyList<GpuSchedulingState> GetStates();
}

public sealed record PowerScheme(Guid Id, string Name);

public interface IPowerSchemeProvider
{
    Guid GetActiveScheme();

    IReadOnlyList<PowerScheme> GetSchemes();
}

public sealed record PowerStatus(bool HasBattery, bool? OnAcPower, int? BatteryPercent, bool EnergySaverOn);

public interface IPowerStatusProvider
{
    PowerStatus GetStatus();
}

public sealed record DeviceGuardStatus(int VbsStatus, IReadOnlyList<int> ServicesConfigured, IReadOnlyList<int> ServicesRunning)
{
    /// <summary>Valeur 2 dans SecurityServices* = intégrité de la mémoire (HVCI).</summary>
    public bool HvciRunning => ServicesRunning.Contains(2);

    /// <summary>0 = désactivé, 1 = activé mais inactif, 2 = actif.</summary>
    public bool VbsRunning => VbsStatus == 2;
}

public interface IDeviceGuardProvider
{
    /// <summary>Null si la classe WMI n'est pas disponible.</summary>
    DeviceGuardStatus? GetStatus();
}

/// <summary>Programme ouvert dans la session de l'utilisateur (regroupé par nom d'exécutable).</summary>
public sealed record RunningProgram(string ExeName, string? Description, string Path, int InstanceCount);

public interface IRunningProgramsProvider
{
    /// <summary>Programmes de la session courante, hors composants de Windows et hors OptiGame.</summary>
    IReadOnlyList<RunningProgram> GetUserPrograms();
}

public interface IRegistryReader
{
    /// <summary>Noms des valeurs d'une clé (vide si la clé n'existe pas).</summary>
    IReadOnlyList<string> GetValueNames(string keyPath);
}

/// <summary>État HDR d'un écran actif (« couleur avancée » de Windows).</summary>
public sealed record DisplayHdrInfo(string Name, bool HdrSupported, bool HdrEnabled)
{
    /// <summary>
    /// Champ « value » de DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO : bit 0 = couleur avancée prise en charge, bit 1 = activée,
    /// bit 2 = couleur étendue IMPOSÉE (gestion des couleurs SDR, pas du HDR). Valeurs réelles de la machine de dev
    /// (2026-09-30) : Samsung LC34G55T en HDR = 0x3 ; BenQ GW2470, écran SDR = 0x5 (bits 0 et 2, sans HDR).
    /// </summary>
    public static DisplayHdrInfo FromAdvancedColor(string name, uint value)
    {
        var wideColorOnly = (value & 4) != 0;
        return new DisplayHdrInfo(name, (value & 1) != 0 && !wideColorOnly, (value & 2) != 0 && !wideColorOnly);
    }
}

public interface IDisplayHdrInfo
{
    IReadOnlyList<DisplayHdrInfo> GetDisplays();
}
