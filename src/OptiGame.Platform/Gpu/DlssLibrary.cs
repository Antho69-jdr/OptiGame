using System.Diagnostics;
using OptiGame.Core.Gpu;

namespace OptiGame.Platform.Gpu;

/// <summary>Bibliothèque DLSS (super-résolution) livrée avec un jeu : chemin et version (« 3.7.20 »).</summary>
public sealed record DlssLibraryInfo(string Path, string Version);

/// <summary>
/// Recherche de nvngx_dlss.dll dans le dossier du jeu (<see cref="DlssOverride.SearchRoot"/>), lecture seule, 10 niveaux au plus.
/// La première trouvée suffit (un jeu n'en livre qu'une pour la super-résolution ; nvngx_dlssd / dlssg = autres fonctions).
/// </summary>
public static class DlssLibrary
{
    public const string FileName = "nvngx_dlss.dll";

    public static DlssLibraryInfo? Find(string exePath)
    {
        var root = DlssOverride.SearchRoot(exePath);
        if (!Directory.Exists(root)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 10, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        try
        {
            var path = Directory.EnumerateFiles(root, FileName, options).FirstOrDefault();
            if (path is null) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return new DlssLibraryInfo(path, $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
