using OptiGame.Core.State;

namespace OptiGame.Core.Settings;

public enum DockEdge
{
    Bottom,
    Top,
    Left,
    Right,
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

    /// <summary>Taille des icônes au repos, en pixels (48, 64 ou 80).</summary>
    public int DockIconSize { get; set; } = 64;

    /// <summary>Le dock se replie au bord de l'écran quand la souris s'en éloigne.</summary>
    public bool DockAutoHide { get; set; } = true;

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
        DockAutoHide = DockAutoHide,
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
