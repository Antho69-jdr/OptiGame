using System.Management;
using OptiGame.Core.Abstractions;

namespace OptiGame.Platform.Wmi;

// Classes WMI vérifiées sur Windows 11 26200 : Win32_PhysicalMemory, Win32_PhysicalMemoryArray,
// Win32_VideoController (root\cimv2) et Win32_DeviceGuard (root\Microsoft\Windows\DeviceGuard).

internal static class Wmi
{
    /// <summary>Exécute la requête et projette chaque objet avant la libération des objets COM.</summary>
    public static List<T> Query<T>(string wql, Func<ManagementBaseObject, T> project, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        var list = new List<T>();
        foreach (var item in results)
        {
            using (item)
            {
                list.Add(project(item));
            }
        }
        return list;
    }

    public static T? Get<T>(ManagementBaseObject obj, string property)
    {
        var value = obj[property];
        return value is null ? default : (T)value;
    }
}

public sealed class WmiMemoryInfoProvider : IMemoryInfoProvider
{
    public MemoryInfo GetMemoryInfo()
    {
        var modules = Wmi.Query(
            "SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, PartNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory",
            m => new MemoryModule(
                Wmi.Get<string>(m, "BankLabel") ?? "",
                Wmi.Get<string>(m, "DeviceLocator") ?? "",
                Wmi.Get<ulong>(m, "Capacity"),
                NullIfZero(Wmi.Get<uint>(m, "Speed")),
                NullIfZero(Wmi.Get<uint>(m, "ConfiguredClockSpeed")),
                Wmi.Get<string>(m, "PartNumber"),
                Wmi.Get<uint>(m, "SMBIOSMemoryType")));

        // Use = 3 : mémoire système (exclut ROM/flash).
        var slots = Wmi.Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray WHERE Use = 3",
            a => (int)Wmi.Get<ushort>(a, "MemoryDevices")).Sum();

        return new MemoryInfo(modules, slots > 0 ? slots : null);
    }

    private static uint? NullIfZero(uint value) => value == 0 ? null : value;
}

public sealed class WmiGpuInfoProvider : IGpuInfoProvider
{
    public IReadOnlyList<GpuAdapter> GetAdapters() =>
        Wmi.Query("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController",
            v => new GpuAdapter(
                Wmi.Get<string>(v, "Name") ?? "?",
                Wmi.Get<string>(v, "DriverVersion"),
                ParseDate(Wmi.Get<string>(v, "DriverDate")),
                Wmi.Get<string>(v, "PNPDeviceID") ?? ""));

    private static DateTime? ParseDate(string? cimDate)
    {
        if (string.IsNullOrEmpty(cimDate)) return null;
        try
        {
            return ManagementDateTimeConverter.ToDateTime(cimDate);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

public sealed class WmiDeviceGuardProvider : IDeviceGuardProvider
{
    public DeviceGuardStatus? GetStatus()
    {
        try
        {
            return Wmi.Query(
                "SELECT VirtualizationBasedSecurityStatus, SecurityServicesConfigured, SecurityServicesRunning FROM Win32_DeviceGuard",
                dg => new DeviceGuardStatus(
                    (int)Wmi.Get<uint>(dg, "VirtualizationBasedSecurityStatus"),
                    ToInts(dg["SecurityServicesConfigured"]),
                    ToInts(dg["SecurityServicesRunning"])),
                @"root\Microsoft\Windows\DeviceGuard").FirstOrDefault();
        }
        catch (ManagementException)
        {
            // Espace de noms absent (éditions ou versions de Windows sans Device Guard).
            return null;
        }
    }

    private static int[] ToInts(object? value) => value is uint[] array ? array.Select(v => (int)v).ToArray() : [];
}
