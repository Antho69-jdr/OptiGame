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

    public static IReadOnlyList<string> SteamLibraries()
    {
        using var steamKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamPath = steamKey?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return [];
        }

        steamPath = Path.GetFullPath(steamPath.Replace('/', '\\'));
        var libraries = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            var root = Vdf.Parse(File.ReadAllText(vdfPath))["libraryfolders"];
            libraries.AddRange(root?.Children.Values.Select(l => l.GetString("path")).OfType<string>() ?? []);
        }

        return libraries
            .Select(p => Path.GetFullPath(p).TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();
    }

    private static IEnumerable<InstalledGame> ScanSteam()
    {
        foreach (var library in SteamLibraries())
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                VdfNode? app;
                try
                {
                    app = Vdf.Parse(File.ReadAllText(manifest))["AppState"];
                }
                catch (Exception ex) when (ex is FormatException or IOException)
                {
                    continue;
                }

                var appId = app?.GetString("appid");
                var name = app?.GetString("name");
                var installDir = app?.GetString("installdir");
                if (appId is null || name is null || installDir is null || NonGameAppIds.Contains(appId)) continue;

                var folder = Path.Combine(steamapps, "common", installDir);
                if (Directory.Exists(folder))
                {
                    yield return new InstalledGame(name, GameSource.Steam, folder, ExeRanking.Rank(name, installDir, FindExes(folder)));
                }
            }
        }
    }

    private static IEnumerable<InstalledGame> ScanFolder(string root)
    {
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(folder);
            yield return new InstalledGame(name, GameSource.Folder, folder, ExeRanking.Rank(name, name, FindExes(folder)));
        }
    }

    private static List<ExeFile> FindExes(string folder)
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
