namespace OptiGame.Core.Updates;

/// <summary>
/// « Démarrer avec Windows » doit lancer la copie INSTALLÉE d'OptiGame. Constaté le 2026-10-10 : la tâche planifiée, créée un jour
/// depuis une copie de développement, lançait encore celle-ci (1.10 / 1.11, incapable de se mettre à jour) alors que la 1.15.0 était
/// installée. La copie installée recâble donc la tâche sur elle-même au démarrage ; une autre copie n'y touche jamais.
/// </summary>
public static class AutoStartTarget
{
    /// <param name="taskCommand">Programme lancé par la tâche ; null = pas de tâche (« Démarrer avec Windows » désactivé : on n'en crée pas).</param>
    /// <param name="runningExe">Programme qui tourne.</param>
    /// <param name="runningIsInstalledCopy">Il est la copie installée par l'installeur, dans Program Files.</param>
    public static bool ShouldRepoint(string? taskCommand, string runningExe, bool runningIsInstalledCopy) =>
        runningIsInstalledCopy && !string.IsNullOrWhiteSpace(taskCommand) && !SamePath(taskCommand, runningExe);

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        var trimmed = path.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }
}
