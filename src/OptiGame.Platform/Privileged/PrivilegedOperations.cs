using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;
using OptiGame.Core;
using OptiGame.Core.Drivers;
using OptiGame.Core.State;
using OptiGame.Core.Updates;
using OptiGame.Platform.Drivers;
using OptiGame.Platform.Registry;
using OptiGame.Platform.Updates;

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

    /// <summary>
    /// Lance, sans aucune fenêtre, l'installeur d'une mise à jour d'OptiGame (il hérite des droits administrateur) : il ferme
    /// OptiGame, le remplace puis le relance. Refusé hors du dossier des mises à jour de la copie installée (Program Files), et
    /// si l'empreinte du fichier n'est plus celle publiée par GitHub.
    /// </summary>
    Process StartAppUpdate(string installerPath, string expectedSha256, bool showWindowAfter);

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

    public Process StartAppUpdate(string installerPath, string expectedSha256, bool showWindowAfter)
    {
        if (InstalledCopy.WhyNoSelfUpdate() is { } reason) throw new InvalidOperationException($"Mise à jour refusée : {reason}.");
        var full = Path.GetFullPath(installerPath);
        var folder = Path.GetFullPath(InstalledCopy.UpdatesFolder).TrimEnd('\\');
        if (!string.Equals(Path.GetDirectoryName(full), folder, StringComparison.OrdinalIgnoreCase) || !AppReleases.IsInstallerName(Path.GetFileName(full)))
        {
            throw new InvalidOperationException($"Mise à jour refusée : l'installeur doit venir du dossier des mises à jour d'OptiGame ({full}).");
        }

        // Ouvert sans partage en écriture ni en suppression, de la vérification au lancement : rien ne peut le modifier, le
        // remplacer ou le renommer entre-temps. L'installeur, lui, s'ouvre en lecture, ce que ce partage autorise.
        using var locked = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexStringLower(SHA256.HashData(locked));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallerRejectedException("L'installeur de la mise à jour a changé depuis sa vérification (empreinte SHA-256 différente) : refusé.");
        }

        var start = new ProcessStartInfo(full) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(full)! };
        foreach (var argument in AppReleases.SilentInstallArguments(showWindowAfter, Path.Combine(paths.LogsDir, "update.log")))
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("L'installeur de la mise à jour ne s'est pas lancé.");
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
