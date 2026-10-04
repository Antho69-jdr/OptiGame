using OptiGame.Core.Library;

namespace OptiGame.Platform.Library;

/// <summary>
/// Jeux installés par les autres lanceurs (lecture seule, sans droits administrateur). Sources vérifiées sur la machine de dev le
/// 2026-10-04 avec un jeu installé par lanceur :
/// <list type="bullet">
/// <item>Epic : <c>C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item</c> (Absolute Drift) ;</item>
/// <item>GOG : HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;id&gt; (gameName, path, exe : WARHAMMER 40K Rites of War) ;</item>
/// <item>Ubisoft : HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\&lt;id&gt;\InstallDir (Steep, 3279) ;</item>
/// <item>EA : sous-dossiers contenant <c>__Installer</c> du dossier d'installation de l'EA app (<c>machine.downloadinplacedir</c>
/// de <c>C:\ProgramData\EA Desktop\machine.ini</c> : C:\Program Files\EA Games\ ; The Sims 3).</item>
/// </list>
/// Lancement : programme du protocole enregistré (HKCR), comme les raccourcis des lanceurs (<see cref="StoreLaunchers"/>).
/// </summary>
public static class StoreLibraries
{
    public static IEnumerable<InstalledGame> ScanInstalled() =>
        Safe(ScanEpic).Concat(Safe(ScanGog)).Concat(Safe(ScanUbisoft)).Concat(Safe(ScanEa));

    /// <summary>Un lanceur illisible n'empêche pas de trouver les jeux des autres.</summary>
    private static IEnumerable<InstalledGame> Safe(Func<IEnumerable<InstalledGame>> scan)
    {
        try
        {
            return scan().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static IEnumerable<InstalledGame> ScanEpic()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) yield break;
        var launcher = ProtocolExe("com.epicgames.launcher");
        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            if (StoreLaunchers.ParseEpicManifest(File.ReadAllText(file)) is not { } game || !Directory.Exists(game.InstallLocation)) continue;
            yield return new InstalledGame(game.Name, GameSource.Epic, game.InstallLocation, Candidates(game.Name, game.InstallLocation, game.ExePath),
                LauncherPath: launcher,
                LaunchArguments: launcher is null ? null : StoreLaunchers.Quoted(StoreLaunchers.EpicUri(game.Namespace, game.CatalogItemId, game.AppName, "launch")));
        }
    }

    private static IEnumerable<InstalledGame> ScanGog()
    {
        using var games = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (games is null) yield break;
        var galaxy = ProtocolExe("goggalaxy");
        foreach (var id in games.GetSubKeyNames())
        {
            using var key = games.OpenSubKey(id);
            if (key?.GetValue("gameName") is not string name || key.GetValue("path") is not string path || !Directory.Exists(path)) continue;
            var exe = key.GetValue("exe") as string;
            yield return new InstalledGame(name, GameSource.Gog, path, Candidates(name, path, exe),
                LauncherPath: galaxy,
                LaunchArguments: galaxy is null ? null : StoreLaunchers.GogRunArguments(id, path));
        }
    }

    private static IEnumerable<InstalledGame> ScanUbisoft()
    {
        using var installs = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (installs is null) yield break;
        var connect = ProtocolExe("uplay");
        foreach (var id in installs.GetSubKeyNames())
        {
            using var key = installs.OpenSubKey(id);
            if (key?.GetValue("InstallDir") is not string dir) continue;
            var folder = Path.GetFullPath(dir.Replace('/', '\\')).TrimEnd('\\');
            if (!Directory.Exists(folder)) continue;
            var name = Path.GetFileName(folder);
            yield return new InstalledGame(name, GameSource.Ubisoft, folder, Candidates(name, folder, null),
                LauncherPath: connect,
                LaunchArguments: connect is null ? null : StoreLaunchers.Quoted(StoreLaunchers.UbisoftLaunchUri(id)));
        }
    }

    private static IEnumerable<InstalledGame> ScanEa()
    {
        var root = EaInstallRoot();
        if (root is null || !Directory.Exists(root)) yield break;
        foreach (var folder in Directory.EnumerateDirectories(root).Where(f => Directory.Exists(Path.Combine(f, "__Installer"))))
        {
            var name = Path.GetFileName(folder);
            // Comme le raccourci créé par l'EA app : l'exe du jeu, sans lanceur.
            yield return new InstalledGame(name, GameSource.Ea, folder, Candidates(name, folder, null));
        }
    }

    /// <summary>« machine.downloadinplacedir=C:\Program Files\EA Games\ » ; dossier par défaut de l'EA app sinon.</summary>
    private static string? EaInstallRoot()
    {
        var ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EA Desktop", "machine.ini");
        if (!File.Exists(ini)) return null;
        var line = File.ReadLines(ini).FirstOrDefault(l => l.StartsWith("machine.downloadinplacedir=", StringComparison.OrdinalIgnoreCase));
        return line is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games")
            : line["machine.downloadinplacedir=".Length..].Trim();
    }

    /// <summary>Exe indiqué par le lanceur en premier (quand il le donne), puis les autres classés par <see cref="ExeRanking"/>.</summary>
    private static IReadOnlyList<ExeFile> Candidates(string name, string folder, string? declaredExe)
    {
        var ranked = ExeRanking.Rank(name, Path.GetFileName(folder), GameLibraryScanner.FindExes(folder));
        if (declaredExe is null || !File.Exists(declaredExe)) return ranked;
        var declared = new ExeFile(Path.GetFullPath(declaredExe), new FileInfo(declaredExe).Length);
        return [declared, .. ranked.Where(e => !e.Path.Equals(declared.Path, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>Programme enregistré pour un protocole (HKCR\&lt;protocole&gt;\shell\open\command) ; null si le lanceur est absent.</summary>
    public static string? ProtocolExe(string scheme)
    {
        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{scheme}\shell\open\command");
        return StoreLaunchers.ExeOfCommand(key?.GetValue(null) as string) is { } exe && File.Exists(exe) ? exe : null;
    }
}
