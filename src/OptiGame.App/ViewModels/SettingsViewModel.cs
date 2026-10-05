using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core;
using OptiGame.Core.Settings;
using OptiGame.Platform.Artwork;
using OptiGame.Platform.Startup;

namespace OptiGame.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AutoStartService _autoStart;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsStore _settings;
    private bool _updating;

    private readonly IgdbClient _igdb;

    public SettingsViewModel(AutoStartService autoStart, IDialogService dialogs, AppPaths paths, AppSettingsStore settings, IgdbClient igdb,
        UpdateService updates)
    {
        Updates = updates;
        _autoStart = autoStart;
        _dialogs = dialogs;
        _settings = settings;
        _igdb = igdb;
        DataFolder = paths.Root;
        _ = RefreshAutoStartAsync(); // schtasks.exe : hors du thread UI, pour ne pas retarder le démarrage
        RefreshGameFolders();
        var current = settings.Get();
        IgdbClientId = current.IgdbClientId ?? "";
        HasIgdbSecret = current.IgdbClientSecretProtected is not null;
    }

    // ---- Mises à jour ----

    public UpdateService Updates { get; }

    public IReadOnlyList<UpdateModeOption> UpdateModeOptions { get; } =
    [
        new(UpdateMode.Automatic, "Installer automatiquement (recommandé)"),
        new(UpdateMode.Notify, "Me prévenir seulement"),
        new(UpdateMode.Off, "Ne pas rechercher"),
    ];

    public UpdateModeOption SelectedUpdateMode
    {
        get => UpdateModeOptions.FirstOrDefault(o => o.Value == _settings.Get().UpdateMode) ?? UpdateModeOptions[0];
        set
        {
            if (value is null || value.Value == _settings.Get().UpdateMode) return;
            _settings.Update(s => s.UpdateMode = value.Value);
            OnPropertyChanged();
            Updates.ModeChanged();
        }
    }

    // ---- Dock ----

    public IReadOnlyList<DockEdgeOption> DockEdgeOptions { get; } =
    [
        new(DockEdge.Bottom, "En bas"),
        new(DockEdge.Top, "En haut"),
        new(DockEdge.Left, "À gauche"),
        new(DockEdge.Right, "À droite"),
    ];

    public IReadOnlyList<DockShapeOption> DockShapeOptions { get; } =
    [
        new(DockIconShape.Square, "Carré"),
        new(DockIconShape.Cover, "Format jaquette (portrait)"),
    ];

    public int MinDockIconSize => Core.Dock.DockLayout.MinIconSize;

    public int MaxDockIconSize => Core.Dock.DockLayout.MaxIconSize;

    public bool DockEnabled
    {
        get => _settings.Get().DockEnabled;
        set { _settings.Update(s => s.DockEnabled = value); OnPropertyChanged(); }
    }

    public bool DockAutoHide
    {
        get => _settings.Get().DockAutoHide;
        set { _settings.Update(s => s.DockAutoHide = value); OnPropertyChanged(); }
    }

    public double MinDockHideDelay => Core.Dock.DockLayout.MinHideDelay;

    public double MaxDockHideDelay => Core.Dock.DockLayout.MaxHideDelay;

    /// <summary>Délai de masquage automatique, en secondes (curseur ; enregistré une fois le curseur immobile).</summary>
    public double DockHideDelay
    {
        get => _settings.Get().DockHideDelay;
        set
        {
            var delay = Math.Round(Core.Dock.DockLayout.HideDelay(value), 1);
            if (Math.Abs(delay - _settings.Get().DockHideDelay) < 0.001) return;
            _settings.Update(s => s.DockHideDelay = delay);
            OnPropertyChanged();
        }
    }

    public bool DockShowOptiGame
    {
        get => _settings.Get().DockShowOptiGame;
        set { _settings.Update(s => s.DockShowOptiGame = value); OnPropertyChanged(); }
    }

    /// <summary>Mesure automatique des FPS pendant les parties (note des jeux).</summary>
    public bool AutoMeasureFps
    {
        get => _settings.Get().AutoMeasureFps;
        set { _settings.Update(s => s.AutoMeasureFps = value); OnPropertyChanged(); }
    }

    public bool LightDuringGames
    {
        get => _settings.Get().LightDuringGames;
        set { _settings.Update(s => s.LightDuringGames = value); OnPropertyChanged(); }
    }

    public bool DockShowNames
    {
        get => _settings.Get().DockShowNames;
        set { _settings.Update(s => s.DockShowNames = value); OnPropertyChanged(); }
    }

    public DockEdgeOption SelectedDockEdge
    {
        get => DockEdgeOptions.FirstOrDefault(o => o.Value == _settings.Get().DockEdge) ?? DockEdgeOptions[0];
        set { if (value is not null) { _settings.Update(s => s.DockEdge = value.Value); OnPropertyChanged(); } }
    }

    public DockShapeOption SelectedDockShape
    {
        get => DockShapeOptions.FirstOrDefault(o => o.Value == _settings.Get().DockIconShape) ?? DockShapeOptions[0];
        set { if (value is not null) { _settings.Update(s => s.DockIconShape = value.Value); OnPropertyChanged(); } }
    }

    /// <summary>Largeur des icônes (curseur ; la liaison attend que le curseur s'arrête avant d'enregistrer).</summary>
    public int DockIconSize
    {
        get => _settings.Get().DockIconSize;
        set
        {
            var size = Math.Clamp(value, MinDockIconSize, MaxDockIconSize);
            if (size == _settings.Get().DockIconSize) return;
            _settings.Update(s => s.DockIconSize = size);
            OnPropertyChanged();
        }
    }

    /// <summary>Opacité du fond du dock, en pourcents (0 à 100).</summary>
    public int DockOpacityPercent
    {
        get => (int)Math.Round(_settings.Get().DockOpacity * 100);
        set
        {
            var opacity = Core.Dock.DockLayout.Opacity(value / 100.0);
            if (Math.Abs(opacity - _settings.Get().DockOpacity) < 0.001) return;
            _settings.Update(s => s.DockOpacity = opacity);
            OnPropertyChanged();
        }
    }

    // ---- IGDB (jaquettes) ----

    [ObservableProperty]
    private string _igdbClientId = "";

    /// <summary>Secret saisi (champ masqué) ; jamais relu depuis le disque, seulement remplacé.</summary>
    public string IgdbClientSecret { get; set; } = "";

    [ObservableProperty]
    private bool _hasIgdbSecret;

    [ObservableProperty]
    private string _igdbStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestIgdbCommand))]
    private bool _isTestingIgdb;

    /// <summary>Enregistre les identifiants (secret chiffré par Windows) puis teste la connexion.</summary>
    [RelayCommand(CanExecute = nameof(CanTestIgdb))]
    private async Task TestIgdbAsync()
    {
        var clientId = IgdbClientId.Trim();
        var secret = IgdbClientSecret.Trim();
        _settings.Update(s =>
        {
            s.IgdbClientId = clientId.Length == 0 ? null : clientId;
            if (secret.Length > 0) s.IgdbClientSecretProtected = SecretProtector.Protect(secret);
        });
        IgdbClientSecret = "";
        HasIgdbSecret = _settings.Get().IgdbClientSecretProtected is not null;
        IgdbCredentialsChanged?.Invoke(this, EventArgs.Empty);

        IsTestingIgdb = true;
        IgdbStatus = "Test en cours…";
        try
        {
            IgdbStatus = "✓ " + await _igdb.TestAsync();
        }
        catch (Exception ex)
        {
            IgdbStatus = "✗ " + ex.Message;
        }
        finally
        {
            IsTestingIgdb = false;
        }
    }

    private bool CanTestIgdb() => !IsTestingIgdb;

    [RelayCommand]
    private void ForgetIgdb()
    {
        if (!_dialogs.Confirm("Supprimer les identifiants IGDB ?",
                "Les jaquettes déjà téléchargées restent ; les nouveaux jeux n'en recevront plus jusqu'à ce que vous saisissiez de nouveaux identifiants.",
                "Supprimer", isDestructive: true))
        {
            return;
        }
        _settings.Update(s =>
        {
            s.IgdbClientId = null;
            s.IgdbClientSecretProtected = null;
        });
        IgdbClientId = "";
        HasIgdbSecret = false;
        IgdbStatus = "Identifiants supprimés.";
    }

    [RelayCommand]
    private static void OpenTwitchConsole() =>
        // explorer.exe transmet l'adresse au navigateur de la session, sans droits administrateur.
        Process.Start(new ProcessStartInfo("explorer.exe", "https://dev.twitch.tv/console/apps") { UseShellExecute = true });

    /// <summary>Les identifiants ont changé : la bibliothèque peut relancer la recherche des jaquettes.</summary>
    public event EventHandler? IgdbCredentialsChanged;

    public string DataFolder { get; }

    public string ExePath { get; } = Environment.ProcessPath ?? "";

    /// <summary>Dossiers dont chaque sous-dossier est un jeu, pour la recherche des jeux installés.</summary>
    public ObservableCollection<string> GameFolders { get; } = [];

    [ObservableProperty]
    private bool _autoStartEnabled;

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_updating) return;
        try
        {
            if (value) _autoStart.Enable(ExePath, App.MinimizedArgument);
            else _autoStart.Disable();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Démarrage automatique non modifié", "La tâche planifiée « OptiGame » n'a pas pu être modifiée.", ex.Message);
        }
        RefreshAutoStart();
    }

    [RelayCommand]
    private void AddGameFolder()
    {
        if (_dialogs.PickFolder("Choisir un dossier contenant des jeux (un sous-dossier par jeu)") is not { } folder) return;
        _settings.Update(s =>
        {
            if (!s.GameFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) s.GameFolders.Add(folder);
        });
        RefreshGameFolders();
    }

    [RelayCommand]
    private void RemoveGameFolder(string folder)
    {
        _settings.Update(s => s.GameFolders.RemoveAll(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase)));
        RefreshGameFolders();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
    }

    private void RefreshGameFolders()
    {
        GameFolders.Clear();
        foreach (var folder in _settings.Get().GameFolders) GameFolders.Add(folder);
    }

    private void RefreshAutoStart() => SetAutoStartSilently(_autoStart.IsEnabled());

    private async Task RefreshAutoStartAsync() => SetAutoStartSilently(await Task.Run(_autoStart.IsEnabled));

    private void SetAutoStartSilently(bool enabled)
    {
        _updating = true;
        try
        {
            AutoStartEnabled = enabled;
        }
        finally
        {
            _updating = false;
        }
    }
}

public sealed record UpdateModeOption(UpdateMode Value, string Label);

public sealed record DockEdgeOption(DockEdge Value, string Label);

public sealed record DockShapeOption(DockIconShape Value, string Label);
