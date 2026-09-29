namespace OptiGame.Core;

/// <summary>Emplacements des fichiers de l'appli, tous sous %LocalAppData%\OptiGame.</summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
    }

    /// <summary>
    /// %LocalAppData%\OptiGame, ou le dossier de la variable OPTIGAME_DATA_DIR (développement : tester l'UI avec des
    /// données fictives sans toucher aux vraies).
    /// </summary>
    public static AppPaths Default { get; } = new(
        Environment.GetEnvironmentVariable("OPTIGAME_DATA_DIR") is { Length: > 0 } devRoot
            ? devRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiGame"));

    public string Root { get; }

    /// <summary>Changements temporaires de la session de jeu en cours (restaurés à la fin ou après un crash).</summary>
    public string SessionJournal => Path.Combine(Root, "session.json");

    /// <summary>Corrections durables du diagnostic, annulables depuis l'UI.</summary>
    public string FixesJournal => Path.Combine(Root, "fixes.json");

    public string Profiles => Path.Combine(Root, "profiles.json");

    public string Settings => Path.Combine(Root, "settings.json");

    public string CapturesDir => Path.Combine(Root, "captures");

    public string LogsDir => Path.Combine(Root, "logs");
}
