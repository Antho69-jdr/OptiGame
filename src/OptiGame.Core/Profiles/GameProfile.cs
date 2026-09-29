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

    public GameProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        ExePath = ExePath,
        Enabled = Enabled,
        PowerSchemeId = PowerSchemeId,
        Priority = Priority,
        ProcessesToClose = ProcessesToClose.Select(p => new ProcessToClose { ExeName = p.ExeName, Relaunch = p.Relaunch }).ToList(),
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
