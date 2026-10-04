using OptiGame.Core.Updates;

namespace OptiGame.Platform.Updates;

/// <summary>Copie d'OptiGame installée par son installeur (Inno Setup) : la seule qui se met à jour elle-même.</summary>
public static class InstalledCopy
{
    /// <summary>Clé de désinstallation qu'Inno Setup écrit, en mode 64 bits, dans la vue 64 bits : « {AppId}_is1 ».</summary>
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{BEC983EC-07E6-4D4C-A824-72F7135018FA}_is1";

    /// <summary>Dossier des installeurs de mise à jour, à côté de l'exe : dans Program Files, seul un administrateur peut y écrire.</summary>
    public static string UpdatesFolder => Path.Combine(AppContext.BaseDirectory, "updates");

    /// <summary>Dossier inscrit par l'installeur (InstallLocation, terminé par « \ »), ou null.</summary>
    public static string? InstallLocation()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(UninstallKey); // OptiGame.Platform.Registry est un espace de noms
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Raison pour laquelle cette copie ne se met pas à jour elle-même (<see cref="UpdatePolicy.WhyNoSelfUpdate"/>), ou null.</summary>
    public static string? WhyNoSelfUpdate() => UpdatePolicy.WhyNoSelfUpdate(
        InstallLocation(), AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
}
