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

/// <summary>
/// Paramètres, en groupes façon Windows 11 (Général, Mes jeux, Dock, Mesures, Mises à jour et à propos, Données) : chaque case
/// s'applique tout de suite ; les sous-réglages se grisent quand leur réglage parent est désactivé.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AutoStartService _autoStart;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsStore _settings;
    private bool _updating;

    private readonly IgdbClient _igdb;

    public SettingsViewModel(AutoStartService autoStart, IDialogService dialogs, AppPaths paths, AppSettingsStore settings, IgdbClient igdb,
        UpdateService updates, MeasuresViewModel measures, ShellAlerts alerts, TrayViewModel tray)
    {
        Updates = updates;
        Measures = measures;
        OpenLogCommand = alerts.OpenLogCommand;
        QuitCommand = tray.ExitCommand;
        measures.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MeasuresViewModel.HasPresentMon)) OnPropertyChanged(nameof(ShowsPresentMonWarning));
        };
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

    /// <summary>PresentMon (chemin, « Parcourir… ») : réglé ici, utilisé par la page Mesures et la mesure automatique.</summary>
    public MeasuresViewModel Measures { get; }

    /// <summary>Journal d'OptiGame (logs\optigame.log), ouvert par l'Explorateur.</summary>
    public System.Windows.Input.ICommand OpenLogCommand { get; }

    /// <summary>« Quitter OptiGame » (même chemin que la zone de notification : garde des modifications, partie restaurée).</summary>
    public System.Windows.Input.ICommand QuitCommand { get; }

    /// <summary>Mesure automatique cochée sans PresentMon : elle ne se ferait pas (dit, plus ignoré en silence).</summary>
    public bool ShowsPresentMonWarning => AutoMeasureFps && !Measures.HasPresentMon;

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
        set { _settings.Update(s => s.DockEnabled = value); OnPropertyChanged(); OnPropertyChanged(nameof(DockAutoHideEnabled)); }
    }

    /// <summary>Délai de masquage modifiable : dock affiché ET masquage automatique.</summary>
    public bool DockAutoHideEnabled => DockEnabled && DockAutoHide;

    public bool DockAutoHide
    {
        get => _settings.Get().DockAutoHide;
        set { _settings.Update(s => s.DockAutoHide = value); OnPropertyChanged(); OnPropertyChanged(nameof(DockAutoHideEnabled)); }
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
        set { _settings.Update(s => s.AutoMeasureFps = value); OnPropertyChanged(); OnPropertyChanged(nameof(ShowsPresentMonWarning)); }
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
    [NotifyPropertyChangedFor(nameof(HasIgdbStatus))]
    private string _igdbStatus = "";

    [ObservableProperty] private Controls.Severity _igdbStatusSeverity = Controls.Severity.Info;

    public bool HasIgdbStatus => IgdbStatus.Length > 0;

    /// <summary>Identifiants déjà enregistrés : le mode d'emploi est replié.</summary>
    public bool IsIgdbConfigured => IgdbClientId.Length > 0 && HasIgdbSecret;

    /// <summary>Le secret vient d'être enregistré : la vue vide le champ masqué (l'état affiché suit l'état réel).</summary>
    public event EventHandler? SecretSaved;

    /// <summary>« Copier » l'adresse de redirection à saisir dans la console Twitch.</summary>
    [RelayCommand]
    private static void CopyRedirectUrl()
    {
        try
        {
            System.Windows.Clipboard.SetText("http://localhost");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Presse-papiers occupé : l'adresse reste affichée à côté du bouton.
        }
    }

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
        SecretSaved?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsIgdbConfigured));
        IgdbCredentialsChanged?.Invoke(this, EventArgs.Empty);

        IsTestingIgdb = true;
        IgdbStatusSeverity = Controls.Severity.Info;
        IgdbStatus = "Test en cours…";
        try
        {
            var result = await _igdb.TestAsync();
            IgdbStatusSeverity = Controls.Severity.Success;
            IgdbStatus = result;
        }
        catch (Exception ex)
        {
            IgdbStatusSeverity = Controls.Severity.Error;
            IgdbStatus = $"Connexion à IGDB impossible : {ex.Message}";
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
        OnPropertyChanged(nameof(IsIgdbConfigured));
        IgdbStatusSeverity = Controls.Severity.Success;
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

    /// <summary>Lecture ou écriture de la tâche planifiée en cours (schtasks.exe, hors du thread de l'interface) : case grisée.</summary>
    [ObservableProperty]
    private bool _isAutoStartBusy = true;

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_updating) return;
        _ = ApplyAutoStartAsync(value);
    }

    private async Task ApplyAutoStartAsync(bool enable)
    {
        IsAutoStartBusy = true;
        try
        {
            await Task.Run(() =>
            {
                if (enable) _autoStart.Enable(ExePath, App.MinimizedArgument);
                else _autoStart.Disable();
            });
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Démarrage automatique non modifié", "La tâche planifiée « OptiGame » n'a pas pu être modifiée.", ex.Message);
        }
        await RefreshAutoStartAsync();
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

    private async Task RefreshAutoStartAsync()
    {
        IsAutoStartBusy = true;
        try
        {
            SetAutoStartSilently(await Task.Run(_autoStart.IsEnabled));
        }
        finally
        {
            IsAutoStartBusy = false;
        }
    }

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
