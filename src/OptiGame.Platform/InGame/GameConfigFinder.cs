using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using OptiGame.Core.InGame;
using OptiGame.Core.Library;

namespace OptiGame.Platform.InGame;

/// <summary>
/// Fichier de réglages d'un jeu SANS définition, cherché là où les jeux les gardent (lecture seule) : dossiers de l'utilisateur
/// (Documents, Documents\My Games, Saved Games, AppData\Roaming, AppData\Local, AppData\LocalLow ; un dossier au nom du jeu, à un
/// ou deux niveaux, « Éditeur\Jeu ») et dossier du jeu (fichiers nommés video / config / settings / options…). Noms cherchés : l'exe
/// et les dossiers du jeu (« ScrapMechanic.exe », « Scrap Mechanic »). Le fichier fait surtout de réglages graphiques l'emporte
/// (<see cref="DetectedSettings.Rank"/>, puis le plus récent) ; fichiers de valeurs par défaut écartés. Résultat gardé 10 minutes par exe. Vérifié le
/// 2026-10-10 : Overwatch (Documents\Overwatch\Settings\Settings_v0.ini), Portal 2 (update\cfg\video.txt), Scrap Mechanic
/// (AppData\Roaming\Axolot Games\Scrap Mechanic\User\User_…\settings.json).
/// </summary>
public static partial class GameConfigFinder
{
    private static readonly ConcurrentDictionary<string, (DateTime At, string? Path)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLife = TimeSpan.FromMinutes(10);

    private const long MaxFileSize = 256 * 1024;
    /// <summary>Fichiers candidats examinés au plus par dossier, et entrées parcourues au plus (dossiers de jeu : milliers de fichiers).</summary>
    private const int MaxFilesExamined = 400;
    private const int MaxEntriesWalked = 20000;

    /// <summary>Dossiers qui ne sont jamais des réglages (sauvegardes, journaux, contenus du jeu).</summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "save", "saves", "savegames", "savedgames", "backup", "backups", "logs", "log", "cache", "caches", "crashes", "crashreports",
        "screenshots", "blueprints", "worlds", "tiles", "progress", "mods", "workshop", "maps", "materials", "models", "sound", "sounds",
        "media", "localization", "shaders", "shadercache", "movies", "videos", "replays", "demos", "resources", "_commonredist",
    };

    /// <summary>Noms trop généraux pour désigner un jeu (dossiers de binaires).</summary>
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin64", "bin32", "binaries", "win64", "win32", "x64", "x86", "release", "retail", "game", "client", "shipping",
        "launcher", "common", "steamapps", "program files", "program files (x86)", "games", "steamlibrary",
    };

    [GeneratedRegex(@"\.(ini|cfg|json|txt|xml|config)$", RegexOptions.IgnoreCase)]
    private static partial Regex CandidateExtension();

    /// <summary>Dans le dossier du jeu, seuls ces noms (sinon des milliers de fichiers de données) ; jamais les valeurs par défaut.</summary>
    [GeneratedRegex("video|config|setting|option|graphic|display|user|pref", RegexOptions.IgnoreCase)]
    private static partial Regex GameFolderFileName();

    [GeneratedRegex("default|preset|template|example|sample", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultsFileName();

    /// <summary>Réglages détectés, ou null (aucun fichier qui parle d'affichage).</summary>
    public static InGameSettings? Read(string exePath) => Read(exePath, null, [], "");

    /// <summary>
    /// Avec, pour un jeu Unity : son dossier LocalLow\Éditeur\Jeu (examiné aussi) et ses préférences du registre (nombres entiers
    /// seulement), ajoutées aux réglages du fichier trouvé ou lues seules s'il n'y en a pas.
    /// </summary>
    public static InGameSettings? Read(string exePath, string? extraFolder, IReadOnlyList<(string Key, string Value)> extraPairs, string extraSource)
    {
        var path = Find(exePath, extraFolder);
        try
        {
            if (path is null) return extraPairs.Count == 0 ? null : DetectedSettings.Interpret(extraPairs, "ses préférences", extraSource, DateTime.Now);
            var pairs = DetectedSettings.Parse(File.ReadAllText(path)).Concat(extraPairs).ToList();
            return DetectedSettings.Interpret(pairs, Path.GetFileName(path), path, File.GetLastWriteTime(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? Find(string exePath, string? extraFolder = null)
    {
        if (Cache.TryGetValue(exePath, out var cached) && DateTime.UtcNow - cached.At < CacheLife) return cached.Path;
        var path = Search(exePath, extraFolder);
        Cache[exePath] = (DateTime.UtcNow, path);
        return path;
    }

    /// <summary>Noms du jeu : celui de l'exe, puis ceux des dossiers qui le contiennent (jusqu'à steamapps\common ou la racine).</summary>
    public static IReadOnlyList<string> NameKeys(string exePath)
    {
        var keys = new List<string>();
        void Add(string name)
        {
            if (GenericNames.Contains(name)) return;
            var key = StoreCatalogs.NameKey(name);
            if (key.Length >= 4 && !keys.Contains(key)) keys.Add(key);
        }
        Add(Path.GetFileNameWithoutExtension(exePath));
        var folder = Path.GetDirectoryName(exePath);
        for (var i = 0; i < 4 && folder is not null; i++)
        {
            var name = Path.GetFileName(folder);
            if (name.Length == 0 || name.Equals("common", StringComparison.OrdinalIgnoreCase)) break;
            Add(name);
            folder = Path.GetDirectoryName(folder);
        }
        return keys;
    }

    private static bool Matches(string folderName, IReadOnlyList<string> keys)
    {
        var key = StoreCatalogs.NameKey(folderName);
        return key.Length >= 4 && keys.Any(k => k == key || (Math.Min(k.Length, key.Length) >= 5 && (k.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(k, StringComparison.Ordinal))));
    }

    private static string? Search(string exePath, string? extraFolder)
    {
        var keys = NameKeys(exePath);
        if (keys.Count == 0) return null;
        var candidates = new List<string>();
        if (extraFolder is not null && Directory.Exists(extraFolder)) Collect(extraFolder, candidates, all: true);

        // Dossiers de l'utilisateur : un dossier au nom du jeu, directement ou sous celui de l'éditeur.
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            documents, Path.Combine(documents, "My Games"), Path.Combine(userProfile, "Saved Games"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InGameSettingsReader.LocalLow(),
        };
        foreach (var root in roots.OfType<string>().Where(Directory.Exists))
        {
            foreach (var (folder, depth) in UserFolders(root))
            {
                // Un dossier au nom du jeu sous un dossier qui l'est déjà est collecté avec lui.
                if (Matches(Path.GetFileName(folder), keys) && (depth == 1 || !Matches(Path.GetFileName(Path.GetDirectoryName(folder)!), keys)))
                {
                    Collect(folder, candidates, all: true);
                }
            }
        }

        // Dossier du jeu (jusqu'à son dossier d'installation) : fichiers aux noms de réglages seulement.
        var install = Path.GetDirectoryName(exePath);
        for (var i = 0; i < 3 && install is not null; i++)
        {
            var parent = Path.GetDirectoryName(install);
            if (parent is null || Path.GetFileName(parent).Equals("common", StringComparison.OrdinalIgnoreCase)) break;
            if (!GenericNames.Contains(Path.GetFileName(install))) break;
            install = parent;
        }
        if (install is not null && Directory.Exists(install)) Collect(install, candidates, all: false);

        string? best = null;
        var bestScore = 0.0;
        var bestTime = DateTime.MinValue;
        foreach (var file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(file);
                var pairs = DetectedSettings.Parse(File.ReadAllText(file));
                if (DetectedSettings.Interpret(pairs, info.Name, file, info.LastWriteTime) is null) continue;
                var score = DetectedSettings.Rank(pairs);
                if (score > bestScore || (score == bestScore && info.LastWriteTime > bestTime)) (best, bestScore, bestTime) = (file, score, info.LastWriteTime);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return best;
    }

    private static readonly ConcurrentDictionary<string, (DateTime At, IReadOnlyList<(string Folder, int Depth)> Folders)> UserFolderCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dossiers d'un dossier de l'utilisateur sur deux niveaux (« Jeu », « Éditeur\Jeu »), relus au plus toutes les 10 minutes.</summary>
    private static IReadOnlyList<(string Folder, int Depth)> UserFolders(string root)
    {
        if (UserFolderCache.TryGetValue(root, out var cached) && DateTime.UtcNow - cached.At < CacheLife) return cached.Folders;
        var folders = new List<(string, int)>();
        foreach (var first in SafeDirectories(root))
        {
            folders.Add((first, 1));
            folders.AddRange(SafeDirectories(first).Select(second => (second, 2)));
        }
        UserFolderCache[root] = (DateTime.UtcNow, folders);
        return folders;
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).Where(d => !SkippedFolders.Contains(Path.GetFileName(d))).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Fichiers candidats sous un dossier (4 niveaux au plus, dossiers de contenu écartés ; 400 candidats, 20 000 entrées au plus).</summary>
    private static void Collect(string folder, List<string> candidates, bool all)
    {
        var queue = new Queue<(string Folder, int Depth)>([(folder, 0)]);
        var examined = 0;
        var walked = 0;
        while (queue.Count > 0 && examined < MaxFilesExamined && walked < MaxEntriesWalked)
        {
            var (current, depth) = queue.Dequeue();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    if (++walked > MaxEntriesWalked) break;
                    var name = Path.GetFileName(file);
                    if (!CandidateExtension().IsMatch(name) || DefaultsFileName().IsMatch(name) || (!all && !GameFolderFileName().IsMatch(name))) continue;
                    if (++examined > MaxFilesExamined) break;
                    if (new FileInfo(file).Length is > 0 and <= MaxFileSize) candidates.Add(file);
                }
                if (depth < 4)
                {
                    foreach (var child in SafeDirectories(current)) queue.Enqueue((child, depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
