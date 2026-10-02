using System.Diagnostics;
using Microsoft.Win32;
using OptiGame.Core;
using OptiGame.Core.Drivers;
using OptiGame.Core.State;
using OptiGame.Platform.Drivers;
using OptiGame.Platform.Registry;

namespace OptiGame.Platform.Privileged;

/// <summary>
/// Point de passage unique de toutes les opérations qui exigent les droits administrateur.
/// En v1, l'appli entière est élevée (manifeste requireAdministrator) et cette interface est implémentée
/// dans le processus. Pour passer en asInvoker, il suffira de l'implémenter via un processus/service élevé.
/// </summary>
public interface IPrivilegedOperations
{
    /// <summary>Écrit une valeur sous HKEY_LOCAL_MACHINE.</summary>
    void WriteMachineRegistryValue(string keyPath, string? name, SettingValue value);

    /// <summary>
    /// Ouvre l'installeur officiel téléchargé, NVIDIA ou AMD (il hérite des droits administrateur). Refusé hors du dossier des
    /// téléchargements d'OptiGame, et si la signature n'est plus valide au moment de l'ouvrir.
    /// </summary>
    Process StartVerifiedInstaller(string path, InstallerVendor vendor);

    /// <summary>Télécharge et installe, par Windows Update, les pilotes choisis par l'utilisateur. Long : hors du thread UI.</summary>
    WindowsUpdateInstallReport InstallWindowsUpdateDrivers(IReadOnlyList<string> updateIds, Action<string> progress);

    /// <summary>Point de restauration du système « installation de pilote ».</summary>
    RestorePointReport CreateRestorePoint(string description);

    /// <summary>Écrit un réglage d'un plan d'alimentation (sur secteur) ; pris en compte tout de suite si le plan est actif.</summary>
    void WriteAcPowerSetting(Guid scheme, Guid subgroup, Guid setting, uint value);
}

public sealed class InProcessPrivilegedOperations(AppPaths paths) : IPrivilegedOperations
{
    public Process StartVerifiedInstaller(string path, InstallerVendor vendor)
    {
        var full = Path.GetFullPath(path);
        var downloads = Path.GetFullPath(paths.DownloadsDir).TrimEnd('\\') + '\\';
        if (!full.StartsWith(downloads, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Installeur refusé : il doit venir du dossier des téléchargements d'OptiGame ({full}).");
        }
        // Revérifié juste avant l'ouverture : le fichier a pu changer depuis le téléchargement.
        if (DriverDownloader.Check(full, vendor) is { } problem) throw new InstallerRejectedException(problem);

        return Process.Start(new ProcessStartInfo(full) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(full)! })
               ?? throw new InvalidOperationException("L'installeur ne s'est pas lancé.");
    }

    public WindowsUpdateInstallReport InstallWindowsUpdateDrivers(IReadOnlyList<string> updateIds, Action<string> progress) =>
        WindowsUpdateDriverInstaller.Install(updateIds, progress);

    public RestorePointReport CreateRestorePoint(string description) => SystemRestore.Create(description);

    public void WriteAcPowerSetting(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        var result = Native.NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value);
        if (result != 0) throw new System.ComponentModel.Win32Exception((int)result, $"PowerWriteACValueIndex a échoué ({result}).");

        // Un plan modifié n'est relu par Windows qu'en le (ré)activant.
        var active = new Power.PowerSchemeService().GetActiveScheme();
        if (active == scheme)
        {
            result = Native.NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            if (result != 0) throw new System.ComponentModel.Win32Exception((int)result, $"PowerSetActiveScheme a échoué ({result}).");
        }
    }

    public void WriteMachineRegistryValue(string keyPath, string? name, SettingValue value)
    {
        if (RegistryIO.Split(keyPath).Hive != RegistryHive.LocalMachine)
        {
            throw new ArgumentException("Seules les clés HKLM passent par les opérations privilégiées.", nameof(keyPath));
        }

        RegistryIO.Write(keyPath, name, value);
    }
}
