namespace OptiGame.Core.Library;

public enum InstallState
{
    Installed,

    /// <summary>L'exe n'existe plus alors que son disque est là : le jeu a été désinstallé (ou déplacé).</summary>
    Uninstalled,

    /// <summary>Le disque du jeu n'est pas branché (disque externe, lecteur réseau) : le jeu n'est PAS considéré comme désinstallé.</summary>
    DriveUnavailable,
}

/// <summary>
/// État d'installation d'un jeu de « Mes jeux », d'après l'exe de son profil. Un profil n'est jamais retiré automatiquement :
/// un jeu désinstallé est signalé, et l'utilisateur choisit de le retirer (ses réglages, mesures et temps de jeu restent sinon).
/// </summary>
public static class GameInstallation
{
    public static InstallState Of(string exePath, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        if (!string.IsNullOrWhiteSpace(exePath) && fileExists(exePath)) return InstallState.Installed;
        return RootOf(exePath) is { } root && !directoryExists(root) ? InstallState.DriveUnavailable : InstallState.Uninstalled;
    }

    /// <summary>
    /// Racine d'un chemin Windows : « A:\ » ou « \\serveur\partage\ » ; null si le chemin n'est pas complet. Écrit à la main
    /// (et non Path.GetPathRoot) pour donner le même résultat quel que soit le système qui exécute les tests.
    /// </summary>
    public static string? RootOf(string path)
    {
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
        {
            return char.ToUpperInvariant(path[0]) + @":\";
        }
        if (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal)
            && !path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            var parts = path[2..].Split('\\', 3);
            if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0) return $@"\\{parts[0]}\{parts[1]}\";
        }
        return null;
    }
}
