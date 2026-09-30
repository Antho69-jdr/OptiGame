using System.Text.RegularExpressions;
using OptiGame.Core.Abstractions;

namespace OptiGame.Core.Drivers;

public enum GpuVendor
{
    Other,
    Nvidia,
    Amd,
    Intel,
}

public enum DriverState
{
    /// <summary>Le pilote installé est le plus récent publié par le fabricant.</summary>
    UpToDate,

    UpdateAvailable,

    /// <summary>Pas de vérification possible (fabricant sans service de recherche, carte inconnue, réseau…).</summary>
    Unknown,
}

/// <summary>Pilote de carte graphique : installé et dernier publié par le fabricant.</summary>
public sealed record GpuDriverStatus(string GpuName, GpuVendor Vendor, string? InstalledVersion, NvidiaDriver? Latest, DriverState State, string Message);

/// <summary>
/// Pilote proposé par Windows Update (API Windows Update Agent, recherche « IsInstalled=0 and Type='Driver' »). Vérifié
/// sur la machine de dev le 2026-09-30 : 4 pilotes, dont « NVIDIA Display Driver Update (32.0.16.1088) », classe Video.
/// </summary>
public sealed record WindowsUpdateDriver(string UpdateId, string Title, string? DriverClass, DateTime? DriverDate, string? Manufacturer,
    string? Model, bool MayRequireReboot, long? SizeBytes)
{
    /// <summary>Version entre parenthèses à la fin du titre (« … Driver Update (32.0.16.1088) »), ou null.</summary>
    public string? Version => Regex.Match(Title, @"\((\d+(?:\.\d+){1,3})\)\s*$") is { Success: true } m ? m.Groups[1].Value : null;
}

public static partial class DriverRules
{
    /// <summary>Fabricant d'après l'identifiant PCI (PCI\VEN_10DE… = NVIDIA).</summary>
    public static GpuVendor VendorOf(string pnpDeviceId) => VendorId().Match(pnpDeviceId) is { Success: true } m
        ? m.Groups[1].Value.ToUpperInvariant() switch
        {
            "10DE" => GpuVendor.Nvidia,
            "1002" => GpuVendor.Amd,
            "8086" => GpuVendor.Intel,
            _ => GpuVendor.Other,
        }
        : GpuVendor.Other;

    /// <summary>État du pilote d'une carte ; <paramref name="latest"/> = dernier pilote NVIDIA publié pour elle (null si inconnu).</summary>
    public static GpuDriverStatus Evaluate(GpuAdapter gpu, NvidiaDriver? latest, string? lookupError = null)
    {
        var vendor = VendorOf(gpu.PnpDeviceId);
        if (vendor != GpuVendor.Nvidia)
        {
            var tool = vendor switch
            {
                GpuVendor.Amd => "AMD Software: Adrenalin Edition",
                GpuVendor.Intel => "Intel Graphics Software (ou Arc Control)",
                _ => "l'outil du fabricant",
            };
            return new(gpu.Name, vendor, gpu.DriverVersion, null, DriverState.Unknown,
                $"Pas de vérification automatique pour ce fabricant : utilisez {tool}.");
        }

        var installed = NvidiaDrivers.FromWindowsVersion(gpu.DriverVersion);
        if (latest is null)
        {
            return new(gpu.Name, vendor, installed ?? gpu.DriverVersion, null, DriverState.Unknown,
                lookupError ?? "Dernier pilote NVIDIA introuvable pour cette carte.");
        }
        if (installed is null || NvidiaDrivers.Compare(latest.Version, installed) is not { } comparison)
        {
            return new(gpu.Name, vendor, gpu.DriverVersion, latest, DriverState.Unknown,
                $"Version installée illisible ({gpu.DriverVersion ?? "?"}) : comparez avec la {latest.Version}.");
        }
        return comparison > 0
            ? new(gpu.Name, vendor, installed, latest, DriverState.UpdateAvailable, $"Nouveau pilote disponible : {latest.Version} (installé : {installed}).")
            : new(gpu.Name, vendor, installed, latest, DriverState.UpToDate, $"Pilote à jour ({installed}).");
    }

    /// <summary>
    /// Pilote graphique NVIDIA de Windows Update rendu inutile par celui de NVIDIA, au moins aussi récent (ex. 610.88 de
    /// Windows Update face au 617.14 de NVIDIA) : il n'est pas proposé, pour ne jamais installer une version plus ancienne
    /// par un autre canal.
    /// </summary>
    public static bool IsSupersededByNvidia(WindowsUpdateDriver update, string? nvidiaLatestVersion)
    {
        if (nvidiaLatestVersion is null || !update.Title.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return false;
        if (update.DriverClass is not { } driverClass || !(driverClass.Equals("Display", StringComparison.OrdinalIgnoreCase) ||
                                                          driverClass.Equals("Video", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return NvidiaDrivers.FromWindowsVersion(update.Version) is { } version && NvidiaDrivers.Compare(version, nvidiaLatestVersion) <= 0;
    }

    [GeneratedRegex(@"VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorId();
}
