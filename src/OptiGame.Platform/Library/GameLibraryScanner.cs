using OptiGame.Core.Library;

namespace OptiGame.Platform.Library;

/// <summary>
/// Recherche des jeux installés (lecture seule), à la demande de l'utilisateur.
/// Steam (vérifié sur la machine de dev) : HKCU\Software\Valve\Steam\SteamPath → steamapps\libraryfolders.vdf
/// (liste des bibliothèques) → steamapps\appmanifest_*.acf (name, installdir) → steamapps\common\installdir.
/// </summary>
public sealed class GameLibraryScanner : IGameLibraryScanner
{
    /// <summary>Applications Steam qui ne sont pas des jeux.</summary>
    private static readonly HashSet<string> NonGameAppIds =
    [
        "228980",  // Steamworks Common Redistributables
        "1070560", // Steam Linux Runtime
        "1391110", // Steam Linux Runtime - Soldier
        "1628350", // Steam Linux Runtime - Sniper
    ];

    private static readonly EnumerationOptions ExeSearch = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 4,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    public IReadOnlyList<InstalledGame> Scan(IReadOnlyList<string> gameFolders)
    {
        var games = new List<InstalledGame>();
        games.AddRange(ScanSteam());
        games.AddRange(StoreLibraries.ScanInstalled());
        foreach (var folder in gameFolders.Where(Directory.Exists))
        {
            games.AddRange(ScanFolder(folder));
        }

        // Un même dossier peut être à la fois une bibliothèque Steam et sous un dossier de jeux.
        return games
            .Where(g => g.Candidates.Count > 0)
            .DistinctBy(g => Path.GetFullPath(g.Folder).TrimEnd('\\'), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> SteamLibraries() => SteamLibraries(SteamPath(), null);

    /// <summary>
    /// Bibliothèques Steam existantes : le dossier de Steam puis celles de libraryfolders.vdf. Un fichier illisible ou un dossier
    /// inaccessible n'empêche jamais de lire les autres (constaté chez un utilisateur : aucun jeu installé trouvé) ; chaque
    /// problème est noté dans <paramref name="problems"/>.
    /// </summary>
    private static IReadOnlyList<string> SteamLibraries(string? steamPath, List<string>? problems)
    {
        if (steamPath is null) return [];
        var libraries = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdfPath)) libraries.AddRange(SteamLibraryFolders.Paths(Vdf.Parse(File.ReadAllText(vdfPath))));
            else problems?.Add("libraryfolders.vdf absent : seule la bibliothèque du dossier de Steam est lue");
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            problems?.Add($"libraryfolders.vdf illisible ({ex.Message}) : seule la bibliothèque du dossier de Steam est lue");
        }

        var existing = new List<string>();
        foreach (var library in libraries)
        {
            string full;
            try
            {
                full = Path.GetFullPath(library).TrimEnd('\\');
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                problems?.Add($"bibliothèque « {library} » : chemin non valide");
                continue;
            }
            if (existing.Contains(full, StringComparer.OrdinalIgnoreCase)) continue;
            if (Directory.Exists(full)) existing.Add(full);
            else problems?.Add($"bibliothèque {full} introuvable (disque débranché ?)");
        }
        return existing;
    }

    /// <summary>Jeu Steam installé, d'après son manifeste appmanifest_*.acf (StateFlags : bit 4 = entièrement installé).</summary>
    public sealed record SteamApp(string AppId, string Name, string InstallDir, string Folder, int? StateFlags = null)
    {
        /// <summary>Entièrement installé (pas en cours de téléchargement) ; manifeste sans StateFlags = supposé installé.</summary>
        public bool IsFullyInstalled => StateFlags is not { } flags || (flags & 4) != 0;
    }

    /// <summary>Ce que la lecture de Steam a trouvé, pour le journal (« Steam : … »).</summary>
    public sealed record SteamScan(string? SteamPath, IReadOnlyList<string> Libraries, IReadOnlyList<SteamApp> Apps, IReadOnlyList<string> Problems)
    {
        public string Describe() => SteamPath is null
            ? "Steam introuvable (ni SteamPath dans HKCU, ni InstallPath dans HKLM)"
            : $"Steam : {SteamPath}, {Libraries.Count} bibliothèque(s) ({string.Join(", ", Libraries)}), {Apps.Count} jeu(x) installé(s)"
              + (Problems.Count > 0 ? " ; " + string.Join(" ; ", Problems) : "");
    }

    /// <summary>Jeux Steam de toutes les bibliothèques (hors redistribuables), dossier d'installation existant.</summary>
    public static IReadOnlyList<SteamApp> SteamApps() => ScanSteamApps().Apps;

    public static SteamScan ScanSteamApps()
    {
        var problems = new List<string>();
        var steamPath = SteamPath();
        var libraries = SteamLibraries(steamPath, problems);
        return new SteamScan(steamPath, libraries, SteamApps(libraries, problems), problems);
    }

    public static IReadOnlyList<SteamApp> SteamApps(IEnumerable<string> libraries) => SteamApps(libraries, null);

    private static IReadOnlyList<SteamApp> SteamApps(IEnumerable<string> libraries, List<string>? problems)
    {
        var apps = new List<SteamApp>();
        foreach (var library in libraries)
        {
            var steamapps = Path.Combine(library, "steamapps");
            List<string> manifests;
            try
            {
                if (!Directory.Exists(steamapps))
                {
                    problems?.Add($"{library} : pas de dossier steamapps");
                    continue;
                }
                manifests = Directory.EnumerateFiles(steamapps, "appmanifest_*.acf").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems?.Add($"{steamapps} illisible ({ex.Message})");
                continue;
            }

            var unreadable = 0;
            foreach (var manifest in manifests)
            {
                VdfNode? app;
                try
                {
                    app = Vdf.Parse(File.ReadAllText(manifest))["AppState"];
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
                {
                    unreadable++;
                    continue;
                }

                var appId = app?.GetString("appid");
                var name = app?.GetString("name");
                var installDir = app?.GetString("installdir");
                if (appId is null || name is null || installDir is null || NonGameAppIds.Contains(appId)) continue;

                var folder = Path.Combine(steamapps, "common", installDir);
                var flags = int.TryParse(app?.GetString("StateFlags"), out var f) ? f : (int?)null;
                if (Directory.Exists(folder)) apps.Add(new SteamApp(appId, name, installDir, folder, flags));
            }
            if (unreadable > 0) problems?.Add($"{steamapps} : {unreadable} manifeste(s) illisible(s)");
        }
        return apps;
    }

    /// <summary>Appid Steam du jeu dont l'exe se trouve dans ce dossier d'installation, ou null.</summary>
    public static string? FindSteamAppId(string exePath, IReadOnlyList<SteamApp> apps)
    {
        var full = Core.Profiles.ExePaths.Normalize(exePath);
        return apps.FirstOrDefault(a => full.StartsWith(Path.GetFullPath(a.Folder).TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase))?.AppId;
    }

    /// <summary>Appid Steam d'un profil : celui du profil s'il est valide, sinon celui du dossier d'installation de l'exe.</summary>
    public static string? SteamAppIdFor(Core.Profiles.GameProfile profile, IReadOnlyList<SteamApp> apps) =>
        profile.SteamAppId is { } id && Core.Launching.LaunchPlanner.IsValidSteamAppId(id) ? id : FindSteamAppId(profile.ExePath, apps);

    /// <summary>
    /// Dossier d'installation de Steam : HKCU\Software\Valve\Steam\SteamPath, sinon HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath
    /// (écrit par l'installeur de Steam, vérifié sur la machine de dev). Le second sert quand OptiGame, élevé avec un AUTRE compte
    /// administrateur, lit le HKCU de ce compte, où Steam n'a jamais tourné. Null si aucun n'existe.
    /// </summary>
    public static string? SteamPath()
    {
        using var userKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var machineKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        foreach (var value in new[] { userKey?.GetValue("SteamPath"), machineKey?.GetValue("InstallPath") })
        {
            if (value is not string path || path.Length == 0) continue;
            try
            {
                var full = Path.GetFullPath(path.Replace('/', '\\')).TrimEnd('\\');
                if (Directory.Exists(full)) return full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return null;
    }

    /// <summary>Chemin de steam.exe (HKCU\Software\Valve\Steam\SteamExe, vérifié sur la machine de dev), ou null.</summary>
    public static string? SteamExe()
    {
        using var steamKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return steamKey?.GetValue("SteamExe") is string exe && exe.Length > 0 && File.Exists(exe)
            ? Path.GetFullPath(exe.Replace('/', '\\'))
            : null;
    }

    private static IEnumerable<InstalledGame> ScanSteam() => SteamApps().Select(ToInstalledGame);

    /// <summary>Jeu Steam avec ses exécutables classés (le premier est le plus probable). Parcourt le dossier : hors du thread UI.</summary>
    public static InstalledGame ToInstalledGame(SteamApp app) =>
        new(app.Name, GameSource.Steam, app.Folder, ExeRanking.Rank(app.Name, app.InstallDir, FindExes(app.Folder)), app.AppId);

    private static IEnumerable<InstalledGame> ScanFolder(string root)
    {
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(folder);
            yield return new InstalledGame(name, GameSource.Folder, folder, ExeRanking.Rank(name, name, FindExes(folder)));
        }
    }

    internal static List<ExeFile> FindExes(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*.exe", ExeSearch).Select(f => new ExeFile(f.FullName, f.Length)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
