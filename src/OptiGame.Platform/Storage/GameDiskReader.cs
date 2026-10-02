using System.Runtime.InteropServices;
using OptiGame.Core.Library;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Storage;

/// <summary>
/// Disque d'installation d'un jeu (lecture seule, sans droits administrateur) : lettre du lecteur → disque physique
/// (MSFT_Partition.DiskNumber = MSFT_PhysicalDisk.DeviceId, root\Microsoft\Windows\Storage), puis questions posées au volume
/// (pénalité de recherche, TRIM) et espace libre. Vérifié sur les 4 disques de la machine de dev le 2026-10-02.
/// </summary>
public static class GameDiskReader
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    /// <summary>Null si le chemin n'a pas de lecteur (ou si le lecteur n'existe plus).</summary>
    public static DiskFacts? Read(string exePath)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(exePath));
        if (string.IsNullOrEmpty(root)) return null;
        if (root.StartsWith(@"\\", StringComparison.Ordinal)) return new DiskFacts(root, null, null, null, null, null, 0, 0, IsNetwork: true);

        var drive = new DriveInfo(root);
        if (!drive.IsReady) return null;
        var letter = char.ToUpperInvariant(root[0]);
        if (drive.DriveType == DriveType.Network)
        {
            return new DiskFacts($"{letter}:", null, null, null, null, null, drive.AvailableFreeSpace, drive.TotalSize, IsNetwork: true);
        }

        var (model, mediaType, busType) = PhysicalDisk(letter);
        var volume = $@"\\.\{letter}:";
        return new DiskFacts($"{letter}:", model, mediaType, busType,
            Ask(volume, StorageDeviceSeekPenaltyProperty), Ask(volume, StorageDeviceTrimProperty),
            drive.AvailableFreeSpace, drive.TotalSize);
    }

    private static (string? Model, int? MediaType, int? BusType) PhysicalDisk(char letter)
    {
        var diskNumber = Wmi.Wmi.Query("SELECT * FROM MSFT_Partition", p => (Letter: Wmi.Wmi.Get<char>(p, "DriveLetter"), Disk: Wmi.Wmi.Get<uint>(p, "DiskNumber")), StorageScope)
            .Where(p => char.ToUpperInvariant(p.Letter) == letter)
            .Select(p => (uint?)p.Disk)
            .FirstOrDefault();
        if (diskNumber is null) return (null, null, null);

        return Wmi.Wmi.Query("SELECT * FROM MSFT_PhysicalDisk",
                d => (Id: Wmi.Wmi.Get<string>(d, "DeviceId"), Model: Wmi.Wmi.Get<string>(d, "FriendlyName")?.Trim(),
                    MediaType: (int?)Wmi.Wmi.Get<ushort>(d, "MediaType"), BusType: (int?)Wmi.Wmi.Get<ushort>(d, "BusType")),
                StorageScope)
            .Where(d => d.Id == diskNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Select(d => (d.Model, d.MediaType, d.BusType))
            .FirstOrDefault();
    }

    /// <summary>Réponse oui/non du périphérique ; null s'il ne sait pas répondre (ex. disque dur WD10EARX : erreur 31, USB 2.0 : erreur 1).</summary>
    private static bool? Ask(string volume, int property)
    {
        // Accès 0 : interroger le périphérique sans l'ouvrir en lecture (pas de droits administrateur nécessaires).
        using var handle = CreateFile(volume, 0, 3 /* lecture | écriture partagées */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var query = new StoragePropertyQuery { PropertyId = property };
        return DeviceIoControl(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
            out var answer, Marshal.SizeOf<StorageBoolDescriptor>(), out var returned, IntPtr.Zero) && returned >= 9
            ? answer.Value
            : null;
    }
}
