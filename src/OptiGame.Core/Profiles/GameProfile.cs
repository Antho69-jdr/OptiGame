namespace OptiGame.Core.Profiles;

/// <summary>Priorités proposées. « Temps réel » est volontairement exclu (peut bloquer le système).</summary>
public enum GamePriority
{
    Normal,
    AboveNormal,
    High,
}

public sealed class ProcessToClose
{
    /// <summary>Nom de l'exécutable, ex. <c>chrome.exe</c>. Toutes ses instances de la session sont fermées.</summary>
    public required string ExeName { get; set; }

    /// <summary>Relancer le programme à la fin de la session (s'il tournait au lancement du jeu).</summary>
    public bool Relaunch { get; set; }
}

public sealed class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    /// <summary>Chemin complet de l'exécutable du jeu (comparaison insensible à la casse).</summary>
    public required string ExePath { get; set; }

    /// <summary>Profil appliqué automatiquement au lancement du jeu.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Plan d'alimentation à activer pendant la session ; null = ne pas changer.</summary>
    public Guid? PowerSchemeId { get; set; }

    public GamePriority Priority { get; set; } = GamePriority.Normal;

    public List<ProcessToClose> ProcessesToClose { get; set; } = [];

    /// <summary>Façon de lancer le jeu depuis OptiGame (voir <see cref="Launching.LaunchPlanner"/>).</summary>
    public Launching.LaunchMode LaunchMode { get; set; } = Launching.LaunchMode.Automatic;

    /// <summary>Appid Steam (connu dès la recherche des jeux installés, sinon retrouvé dans les manifestes).</summary>
    public string? SteamAppId { get; set; }

    /// <summary>Programme à lancer en mode « Lanceur » (ex. RSI Launcher.exe).</summary>
    public string? LauncherPath { get; set; }

    /// <summary>Arguments de lancement (transmis au jeu, au lanceur, ou après l'appid pour Steam).</summary>
    public string? LaunchArguments { get; set; }

    /// <summary>Position dans le dock flottant ; null = non épinglé.</summary>
    public int? DockOrder { get; set; }

    /// <summary>Jeu IGDB associé (jaquette et bannière) ; null = pas encore cherché ou non trouvé.</summary>
    public long? IgdbGameId { get; set; }

    public string? CoverImageId { get; set; }

    public string? HeroImageId { get; set; }

    public GameProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        ExePath = ExePath,
        Enabled = Enabled,
        PowerSchemeId = PowerSchemeId,
        Priority = Priority,
        ProcessesToClose = ProcessesToClose.Select(p => new ProcessToClose { ExeName = p.ExeName, Relaunch = p.Relaunch }).ToList(),
        LaunchMode = LaunchMode,
        SteamAppId = SteamAppId,
        LauncherPath = LauncherPath,
        LaunchArguments = LaunchArguments,
        DockOrder = DockOrder,
        IgdbGameId = IgdbGameId,
        CoverImageId = CoverImageId,
        HeroImageId = HeroImageId,
    };

    public bool Matches(string exePath) => ExePaths.AreSame(ExePath, exePath);
}

public sealed class ProfilesDocument
{
    public int Version { get; set; } = 1;

    public List<GameProfile> Profiles { get; set; } = [];
}

public static class ExePaths
{
    /// <summary>Compare deux chemins d'exe après normalisation (ex. « Scrap Mechanic\.\Release\… »).</summary>
    public static bool AreSame(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
