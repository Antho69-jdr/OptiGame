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

public interface IRegistryReader
{
    /// <summary>Noms des valeurs d'une clé (vide si la clé n'existe pas).</summary>
    IReadOnlyList<string> GetValueNames(string keyPath);
}
