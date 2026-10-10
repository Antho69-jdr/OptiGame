namespace OptiGame.Core.Library;

/// <summary>
/// Bibliothèques listées dans <c>steamapps\libraryfolders.vdf</c>. Format actuel : « libraryfolders » { "0" { "path" "…" } … } ;
/// ancien format (Steam d'avant 2021, encore présent si le fichier n'a pas été réécrit) : « LibraryFolders » { "1" "D:\\Jeux" … }
/// à côté de valeurs qui ne sont pas des dossiers (TimeNextStatsReport, ContentStatsID).
/// </summary>
public static class SteamLibraryFolders
{
    public static IReadOnlyList<string> Paths(VdfNode root)
    {
        var folders = root["libraryfolders"];
        if (folders is null) return [];
        var paths = new List<string>();
        foreach (var (key, node) in folders.Children)
        {
            // Entrées numérotées seulement : les autres clés de l'ancien format ne sont pas des dossiers.
            if (!int.TryParse(key, out _)) continue;
            var path = node.Children.Count > 0 ? node.GetString("path") : node.Value;
            if (!string.IsNullOrWhiteSpace(path)) paths.Add(path.Replace('/', '\\'));
        }
        return paths;
    }
}
