using OptiGame.Core.State;

namespace OptiGame.Core.Settings;

public enum DockEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

public enum DockIconShape
{
    /// <summary>Jaquette recadrée au carré.</summary>
    Square,

    /// <summary>Jaquette entière, au format portrait 3:4 des jaquettes IGDB.</summary>
    Cover,
}

/// <summary>Préférences de l'appli (settings.json).</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Dossiers dont chaque sous-dossier est considéré comme un jeu (ex. A:\Jeux).</summary>
    public List<string> GameFolders { get; set; } = [];

    /// <summary>Chemin de PresentMon (console), pour les mesures.</summary>
    public string? PresentMonPath { get; set; }

    /// <summary>Identifiant d'application Twitch, pour IGDB (jaquettes).</summary>
    public string? IgdbClientId { get; set; }

    /// <summary>Secret Twitch chiffré par Windows (DPAPI, compte de l'utilisateur), en base 64. Jamais en clair.</summary>
    public string? IgdbClientSecretProtected { get; set; }

    /// <summary>Dock flottant affiché (désactivé par défaut).</summary>
    public bool DockEnabled { get; set; }

    public DockEdge DockEdge { get; set; } = DockEdge.Bottom;

    /// <summary>Largeur des icônes au repos, en pixels (32 à 128).</summary>
    public int DockIconSize { get; set; } = 64;

    /// <summary>Carré (jaquette recadrée) ou format d'origine de la jaquette (portrait 3:4).</summary>
    public DockIconShape DockIconShape { get; set; } = DockIconShape.Square;

    /// <summary>Opacité du fond du dock, de 0 à 1 (les icônes restent opaques).</summary>
    public double DockOpacity { get; set; } = 0.9;

    /// <summary>Le dock se replie au bord de l'écran quand la souris s'en éloigne ; sinon, il reste collé au bureau (derrière les fenêtres).</summary>
    public bool DockAutoHide { get; set; } = true;

    /// <summary>Délai avant que le dock se replie une fois la souris partie, en secondes (0,2 à 5).</summary>
    public double DockHideDelay { get; set; } = 0.7;

    /// <summary>Raccourci vers OptiGame affiché au bout du dock.</summary>
    public bool DockShowOptiGame { get; set; } = true;

    /// <summary>Nom du jeu affiché au survol d'une jaquette du dock.</summary>
    public bool DockShowNames { get; set; } = true;

    /// <summary>
    /// Jeux Steam déjà vus (installés au premier passage, proposés, ajoutés ou ignorés) : jamais reproposés. Null = premier
    /// passage pas encore fait (les jeux déjà installés sont alors mémorisés sans être proposés).
    /// </summary>
    public List<string>? SteamKnownAppIds { get; set; }

    /// <summary>Mesure automatique des FPS pendant les parties (1 min après 4 min de jeu), pour la note des jeux.</summary>
    public bool AutoMeasureFps { get; set; } = true;

    /// <summary>Diagnostic en vue « Avancé » (chaque correction à la main) plutôt que « Simple » (un bouton).</summary>
    public bool DiagnosticAdvanced { get; set; }

    /// <summary>« Mes jeux » affiche aussi les jeux Steam possédés mais non installés (jaquettes grisées).</summary>
    public bool LibraryShowUninstalled { get; set; }

    public AppSettings Clone() => new()
    {
        Version = Version,
        GameFolders = [.. GameFolders],
        PresentMonPath = PresentMonPath,
        IgdbClientId = IgdbClientId,
        IgdbClientSecretProtected = IgdbClientSecretProtected,
        DockEnabled = DockEnabled,
        DockEdge = DockEdge,
        DockIconSize = DockIconSize,
        DockIconShape = DockIconShape,
        DockOpacity = DockOpacity,
        DockAutoHide = DockAutoHide,
        DockHideDelay = DockHideDelay,
        DockShowOptiGame = DockShowOptiGame,
        DockShowNames = DockShowNames,
        SteamKnownAppIds = SteamKnownAppIds is null ? null : [.. SteamKnownAppIds],
        AutoMeasureFps = AutoMeasureFps,
        DiagnosticAdvanced = DiagnosticAdvanced,
        LibraryShowUninstalled = LibraryShowUninstalled,
    };
}

public sealed class AppSettingsStore(IStateStore<AppSettings> store)
{
    private readonly Lock _lock = new();
    private AppSettings _settings = store.Load() ?? new AppSettings();

    public AppSettings Get()
    {
        lock (_lock) return _settings.Clone();
    }

    public void Update(Action<AppSettings> change)
    {
        lock (_lock)
        {
            var copy = _settings.Clone();
            change(copy);
            store.Save(copy);
            _settings = copy;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
}
