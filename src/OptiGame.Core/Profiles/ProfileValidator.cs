namespace OptiGame.Core.Profiles;

public static class ProfileValidator
{
    /// <summary>
    /// Processus que l'appli refuse de fermer : composants de Windows dont l'arrêt rendrait la session instable,
    /// et OptiGame lui-même. (La couche Platform refuse en plus tout exécutable situé sous le dossier Windows.)
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe",
        "lsaiso.exe", "svchost.exe", "dwm.exe", "explorer.exe", "sihost.exe", "fontdrvhost.exe", "ctfmon.exe",
        "conhost.exe", "taskhostw.exe", "runtimebroker.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
        "searchhost.exe", "textinputhost.exe", "audiodg.exe", "spoolsv.exe", "msmpeng.exe", "securityhealthservice.exe",
        "securityhealthsystray.exe", "memory compression", "optigame.exe",
    };

    public static bool IsProtected(string exeName) => ProtectedProcesses.Contains(exeName.Trim());

    public static IReadOnlyList<string> Validate(GameProfile profile, IReadOnlyList<GameProfile> existing)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("Le nom du profil est vide.");
        }

        if (string.IsNullOrWhiteSpace(profile.ExePath) || !profile.ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("L'exécutable du jeu doit être un fichier .exe.");
        }
        else if (existing.Any(p => p.Id != profile.Id && p.Matches(profile.ExePath)))
        {
            errors.Add("Un autre profil utilise déjà cet exécutable.");
        }

        if (!Enum.IsDefined(profile.Priority))
        {
            errors.Add("Priorité invalide.");
        }

        var gameExe = Path.GetFileName(profile.ExePath ?? "");
        foreach (var process in profile.ProcessesToClose)
        {
            var name = process.ExeName?.Trim() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(['\\', '/', ':']) >= 0)
            {
                errors.Add($"« {name} » n'est pas un nom d'exécutable valide (ex. chrome.exe).");
            }
            else if (IsProtected(name))
            {
                errors.Add($"« {name} » est un composant de Windows ou d'OptiGame : il ne peut pas être fermé.");
            }
            else if (name.Equals(gameExe, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"« {name} » est le jeu lui-même.");
            }
        }

        var duplicates = profile.ProcessesToClose
            .GroupBy(p => p.ExeName?.Trim() ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        errors.AddRange(duplicates.Select(d => $"« {d} » figure plusieurs fois dans la liste."));

        return errors;
    }
}
