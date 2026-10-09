using OptiGame.Core.State;

namespace OptiGame.Core.Settings;

public enum DockEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>Page affichée à l'ouverture de la fenêtre d'OptiGame (Paramètres › Général).</summary>
public enum StartPage
{
    Games,
    Diagnostic,
    Drivers,
    Measures,
}

/// <summary>Animations de l'interface (grossissement et glissement du dock, onde au clic, zoom des jaquettes).</summary>
public enum UiAnimations
{
    /// <summary>Selon « Effets d'animation » de Windows (Accessibilité) : coupées si Windows les coupe.</summary>
    FollowWindows,

    /// <summary>Toujours : par ex. AtlasOS coupe les effets de Windows, mais l'utilisateur veut ceux d'OptiGame.</summary>
    Always,

    Never,
}

/// <summary>Aspect des jaquettes réglable par l'utilisateur.</summary>
public static class CoverStyle
{
    public const int DefaultRadius = 8;
    public const int MaxRadius = 24;

    /// <summary>Arrondi ramené entre 0 (coins droits) et 24 px.</summary>
    public static int Radius(int radius) => Math.Clamp(radius, 0, MaxRadius);
}

/// <summary>Décision : animer ou non, d'après le réglage d'OptiGame et celui de Windows.</summary>
public static class Motion
{
    public static bool IsEnabled(UiAnimations mode, bool windowsAnimations) => mode switch
    {
        UiAnimations.Always => true,
        UiAnimations.Never => false,
        _ => windowsAnimations,
    };
}

public enum DockIconShape
{
    /// <summary>Jaquette recadrée au carré.</summary>
    Square,

    /// <summary>Jaquette entière, au format portrait 3:4 des jaquettes IGDB.</summary>
    Cover,
}

/// <summary>Mises à jour d'OptiGame (versions publiées sur GitHub).</summary>
public enum UpdateMode
{
    /// <summary>Téléchargée puis installée d'elle-même, quand aucun jeu ne tourne et que la fenêtre d'OptiGame est fermée.</summary>
    Automatic,

    /// <summary>Signalée (bandeau, notification) ; installée quand l'utilisateur clique.</summary>
    Notify,

    /// <summary>Aucune recherche automatique (« Rechercher maintenant » reste possible).</summary>
    Off,
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

    /// <summary>Arrondi des coins des icônes du dock, en pixels (14 par défaut ; DockLayout.CornerRadius le borne).</summary>
    public int DockCornerRadius { get; set; } = Dock.DockLayout.DefaultCornerRadius;

    /// <summary>
    /// Jeux Steam déjà vus (installés au premier passage, proposés, ajoutés ou ignorés) : jamais reproposés. Null = premier
    /// passage pas encore fait (les jeux déjà installés sont alors mémorisés sans être proposés).
    /// </summary>
    public List<string>? SteamKnownAppIds { get; set; }

    /// <summary>Mesure automatique des FPS pendant les parties (1 min après 4 min de jeu), pour la note des jeux.</summary>
    public bool AutoMeasureFps { get; set; } = true;

    /// <summary>
    /// Pendant une partie, la fenêtre et le dock d'OptiGame sont fermés (et non masqués) pour libérer leur mémoire ; la fenêtre
    /// revient à la fin de la partie si elle était ouverte. Absent d'un ancien settings.json = activé.
    /// </summary>
    public bool LightDuringGames { get; set; } = true;

    /// <summary>Animations de l'interface : selon Windows par défaut (accessibilité).</summary>
    public UiAnimations UiAnimations { get; set; } = UiAnimations.FollowWindows;

    /// <summary>Page d'ouverture de la fenêtre ; absente d'un ancien settings.json = Mes jeux.</summary>
    public StartPage StartPage { get; set; } = StartPage.Games;

    /// <summary>Arrondi des coins des jaquettes de Mes jeux et de la fiche, en pixels (CoverStyle.Radius le borne).</summary>
    public int CoverCornerRadius { get; set; } = CoverStyle.DefaultRadius;

    /// <summary>Ancien choix de vue du Diagnostic (Simple / Avancé) : plus utilisé depuis la page unique (1.5), gardé pour relire les anciens fichiers.</summary>
    public bool DiagnosticAdvanced { get; set; }

    /// <summary>« Mes jeux » affiche aussi les jeux Steam possédés mais non installés (jaquettes grisées).</summary>
    public bool LibraryShowUninstalled { get; set; }

    /// <summary>Absent d'un ancien settings.json = automatique.</summary>
    public UpdateMode UpdateMode { get; set; } = UpdateMode.Automatic;

    /// <summary>Version d'OptiGame au dernier démarrage : une version plus récente au démarrage suivant = mise à jour installée.</summary>
    public string? LastRunVersion { get; set; }

    /// <summary>Place de la fenêtre principale à sa dernière fermeture ; null = jamais fermée (taille par défaut, centrée).</summary>
    public WindowPlacement? MainWindowPlacement { get; set; }

    /// <summary>L'utilisateur a déjà été prévenu qu'en fermant la fenêtre, OptiGame continue dans la zone de notification.</summary>
    public bool CloseToTrayExplained { get; set; }

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
        DockCornerRadius = DockCornerRadius,
        SteamKnownAppIds = SteamKnownAppIds is null ? null : [.. SteamKnownAppIds],
        AutoMeasureFps = AutoMeasureFps,
        LightDuringGames = LightDuringGames,
        UiAnimations = UiAnimations,
        StartPage = StartPage,
        CoverCornerRadius = CoverCornerRadius,
        DiagnosticAdvanced = DiagnosticAdvanced,
        LibraryShowUninstalled = LibraryShowUninstalled,
        UpdateMode = UpdateMode,
        LastRunVersion = LastRunVersion,
        MainWindowPlacement = MainWindowPlacement is { } p
            ? new WindowPlacement { Left = p.Left, Top = p.Top, Width = p.Width, Height = p.Height, Maximized = p.Maximized }
            : null,
        CloseToTrayExplained = CloseToTrayExplained,
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
