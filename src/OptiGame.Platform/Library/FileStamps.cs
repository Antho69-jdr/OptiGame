namespace OptiGame.Platform.Library;

/// <summary>
/// Empreinte de fichiers ou dossiers (chemin, date de dernière écriture, taille) : sert à ne relire un cache de lanceur que
/// s'il a changé. Un élément absent compte aussi (son apparition change l'empreinte).
/// </summary>
internal static class FileStamps
{
    public static string Of(params string[] paths) => string.Join("|", paths.Select(Stamp));

    private static string Stamp(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists) return $"{path}:{info.LastWriteTimeUtc.Ticks}:{info.Length}";
        var directory = new DirectoryInfo(path);
        return directory.Exists ? $"{path}:{directory.LastWriteTimeUtc.Ticks}:d" : $"{path}:absent";
    }
}
