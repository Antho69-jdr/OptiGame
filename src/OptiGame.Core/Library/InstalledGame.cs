namespace OptiGame.Core.Library;

public enum GameSource
{
    Steam,
    Epic,
    Gog,
    Ubisoft,
    Ea,
    Folder,
}

public sealed record ExeFile(string Path, long SizeBytes);

/// <summary>Jeu trouvé sur le disque, avec ses exécutables candidats classés du plus au moins probable.</summary>
/// <param name="LauncherPath">Programme qui lance le jeu à la place de l'exe (lanceur du magasin), avec <paramref name="LaunchArguments"/> ;
/// null = lancer l'exe (ou Steam pour un jeu Steam). Même commande que les raccourcis créés par le lanceur.</param>
public sealed record InstalledGame(string Name, GameSource Source, string Folder, IReadOnlyList<ExeFile> Candidates, string? SteamAppId = null,
    string? LauncherPath = null, string? LaunchArguments = null);

public interface IGameLibraryScanner
{
    /// <summary>Jeux Steam de toutes les bibliothèques, puis sous-dossiers des dossiers donnés.</summary>
    IReadOnlyList<InstalledGame> Scan(IReadOnlyList<string> gameFolders);
}

/// <summary>
/// Choix de l'exécutable principal d'un jeu. Les manifestes Steam ne l'indiquent pas ; l'heuristique a été
/// validée sur des cas réels : Overwatch, PUBG (TslGame.exe et non le lanceur ExecPubg.exe), Scrap Mechanic,
/// Void Crew (jeu Unity dont l'exe est plus petit que UnityCrashHandler64.exe), Star Citizen (dossier RSI).
/// </summary>
public static class ExeRanking
{
    /// <summary>Fragments de nom d'utilitaires qui ne sont jamais le jeu.</summary>
    private static readonly string[] ExcludedFragments =
    [
        "crash", "unins", "setup", "redist", "dxsetup", "directx", "dotnet", "vcredist", "easyanticheat", "eac_",
        "battleye", "beservice", "bssndrpt", "blizzarderror", "bugreport", "errorreport", "helper", "updater",
        "browser", "cefprocess", "notification", "editor", "compiler", "builder", "installer", "prereq", "benchmark",
    ];

    public static IReadOnlyList<ExeFile> Rank(string gameName, string folderName, IEnumerable<ExeFile> exes, int max = 6)
    {
        var names = new[] { Normalize(gameName), Normalize(folderName) };

        // Un exe qui porte exactement le nom du jeu n'est jamais exclu (ex. un jeu nommé « … Builder »). Le dossier « __Installer »
        // de l'EA app (Touchup.exe, Cleanup.exe, redistribuables : The Sims 3) ne contient jamais le jeu.
        var candidates = exes
            .Where(e => !e.Path.Contains(@"\__Installer\", StringComparison.OrdinalIgnoreCase))
            .Where(e => IsExactName(e, names) || !IsExcluded(Path.GetFileNameWithoutExtension(e.Path)))
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var maxSize = Math.Max(1, candidates.Max(e => e.SizeBytes));

        return candidates
            .Select(e => (Exe: e, Score: Score(e, names, maxSize)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Exe.SizeBytes)
            .Take(max)
            .Select(x => x.Exe)
            .ToList();
    }

    private static double Score(ExeFile exe, string[] gameNames, long maxSize)
    {
        var baseName = Path.GetFileNameWithoutExtension(exe.Path);
        var normalized = Normalize(baseName);
        var score = 40.0 * exe.SizeBytes / maxSize;

        if (IsExactName(exe, gameNames))
        {
            score += 100;
        }
        else if (gameNames.Any(n => n.Length >= 4 && normalized.Length >= 4 && (n.Contains(normalized) || normalized.Contains(n))))
        {
            score += 10;
        }

        if (baseName.Contains("launcher", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }
        return score;
    }

    private static bool IsExactName(ExeFile exe, string[] gameNames)
    {
        var normalized = Normalize(Path.GetFileNameWithoutExtension(exe.Path));
        return gameNames.Any(n => n.Length > 0 && n == normalized);
    }

    private static bool IsExcluded(string baseName) =>
        ExcludedFragments.Any(f => baseName.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
