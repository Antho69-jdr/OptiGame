using System.Management;
using OptiGame.Core.Drivers;

namespace OptiGame.Platform.Drivers;

/// <summary>
/// Points de restauration du système avant l'installation d'un pilote.
/// <list type="bullet">
/// <item>Protection activée : HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients liste les volumes protégés
/// (valeur « {09F7EDC5-…} »). Lisible SEULEMENT avec les droits administrateur (accès refusé sinon, vérifié le
/// 2026-09-30) : l'état réel de la machine de dev est écrit dans le journal à chaque installation.</item>
/// <item>Création : WMI root\default:SystemRestore.CreateRestorePoint (classe présente sur la machine de dev ; création
/// pas encore testée en réel). Windows n'en crée pas plus d'un par 24 h : on le vérifie et on le dit.</item>
/// </list>
/// </summary>
public static class SystemRestore
{
    private const string SppClients = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients";
    private const string SystemRestoreClient = "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}";

    public static RestorePointAvailability Availability()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SppClients);
            return key?.GetValue(SystemRestoreClient) is string[] { Length: > 0 } volumes && volumes.Any(v => v.Length > 0)
                ? RestorePointAvailability.Available
                : RestorePointAvailability.ProtectionDisabled;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return RestorePointAvailability.Unknown; // sans droits administrateur (ex. instance de test)
        }
    }

    /// <summary>Crée un point « installation de pilote ». Droits administrateur requis (via IPrivilegedOperations).</summary>
    internal static RestorePointReport Create(string description)
    {
        var before = LatestSequence();
        using var restore = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
        using var input = restore.GetMethodParameters("CreateRestorePoint");
        input["Description"] = description;
        input["RestorePointType"] = 10;  // DEVICE_DRIVER_INSTALL
        input["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
        using var output = restore.InvokeMethod("CreateRestorePoint", input, null);
        var code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u);
        if (code != 0) return new RestorePointReport(false, $"Windows a refusé de créer le point de restauration (code {code}).");

        return LatestSequence() > before
            ? new RestorePointReport(true, "Point de restauration créé.")
            : new RestorePointReport(false, "Windows n'a pas créé de nouveau point : il n'en crée qu'un par 24 heures, et un point récent existe déjà.");
    }

    private static uint LatestSequence()
    {
        using var searcher = new ManagementObjectSearcher(@"\\.\root\default", "SELECT * FROM SystemRestore");
        uint latest = 0;
        foreach (var point in searcher.Get())
        {
            using (point) latest = Math.Max(latest, Convert.ToUInt32(point["SequenceNumber"] ?? 0u));
        }
        return latest;
    }
}
