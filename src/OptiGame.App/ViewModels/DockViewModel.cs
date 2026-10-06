using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Artwork;
using OptiGame.Core.Library;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Platform.Artwork;

namespace OptiGame.App.ViewModels;

/// <summary>Contenu du dock flottant : jeux épinglés (ordre du profil), état de chacun, actions.</summary>
public sealed partial class DockViewModel : ObservableObject
{
    /// <summary>« Lancement… » reste affiché au plus ce temps (le jeu ou son lanceur peut mettre longtemps à s'ouvrir).</summary>
    private static readonly TimeSpan LaunchFeedback = TimeSpan.FromSeconds(12);

    private readonly ProfileStore _store;
    private readonly ArtworkCache _artwork;
    private readonly LibraryViewModel _library;
    private readonly SettingsViewModel _settingsPage;
    private readonly AppSettingsStore _settings;
    private readonly NavigationService _navigation;
    private readonly IDialogService _dialogs;

    public DockViewModel(ProfileStore store, ArtworkCache artwork, GameSessionManager sessions, LibraryViewModel library,
        SettingsViewModel settingsPage, AppSettingsStore settings, NavigationService navigation, IDialogService dialogs)
    {
        _store = store;
        _artwork = artwork;
        _library = library;
        _settingsPage = settingsPage;
        _settings = settings;
        _navigation = navigation;
        _dialogs = dialogs;
        store.DockChanged += (_, _) => OnUi(Reload);
        store.ArtworkChanged += (_, _) => OnUi(Reload);
        store.Changed += (_, _) => OnUi(Reload); // nom modifié, jeu retiré, exe changé…
        sessions.SessionStarted += (_, _) => OnUi(EndLaunchFeedback); // la partie a commencé : le dock se masque
        Reload();
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Clic sur une jaquette : un seul lancement à la fois par jeu, « Lancement… » affiché jusqu'au début de la partie.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)] // un jeu en cours de lancement ne bloque pas les autres
    private async Task Launch(DockItemViewModel item)
    {
        if (item.IsLaunching) return;
        item.IsLaunching = true;
        var launched = await _library.PlayAsync(item.Id);
        if (!launched)
        {
            item.IsLaunching = false;
            return;
        }
        await Task.Delay(LaunchFeedback);
        item.IsLaunching = false;
    }

    [RelayCommand]
    private void OpenGame(DockItemViewModel item)
    {
        ((App)Application.Current).ShowMainWindow();
        _library.ShowGame(item.Id);
    }

    [RelayCommand]
    private void Unpin(DockItemViewModel item) => _store.SetPinned(item.Id, false);

    [RelayCommand]
    private static void OpenOptiGame() => ((App)Application.Current).ShowMainWindow();

    /// <summary>Dock vide : « Mes jeux », d'où l'on épingle des jeux.</summary>
    [RelayCommand]
    private void OpenLibrary()
    {
        ((App)Application.Current).ShowMainWindow();
        _navigation.Navigate(_library);
        _library.ReturnToGrid();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        ((App)Application.Current).ShowMainWindow();
        _settingsPage.SelectedTab = SettingsTab.Dock; // « Paramètres du dock… » : directement sur l'onglet Dock
        _navigation.Navigate(_settingsPage);
    }

    public bool AutoHide => _settings.Get().DockAutoHide;

    [RelayCommand]
    private void ToggleAutoHide() => _settings.Update(s => s.DockAutoHide = !s.DockAutoHide);

    [RelayCommand]
    private void Disable()
    {
        if (!_dialogs.Confirm("Désactiver le dock ?", "Vous pourrez le réactiver dans Paramètres, section Dock. Vos jeux épinglés sont conservés.",
                "Désactiver le dock"))
        {
            return;
        }
        _settings.Update(s => s.DockEnabled = false);
    }

    /// <summary>Clic droit › Déplacer : un cran vers le début (-1) ou la fin (+1) du dock.</summary>
    public void MoveBy(DockItemViewModel item, int delta)
    {
        var index = Items.IndexOf(item);
        if (index < 0) return;
        Move(item.Id, index + delta);
    }

    /// <summary>
    /// Glisser-déposer dans le dock : l'élément est déplacé tout de suite (le dock anime son arrivée), puis l'ordre est
    /// enregistré ; le rechargement qui suit ne reconstruit rien puisque l'ordre affiché est déjà le bon.
    /// </summary>
    public void Move(Guid id, int newIndex)
    {
        var from = Items.ToList().FindIndex(i => i.Id == id);
        if (from < 0) return;
        var to = Math.Clamp(newIndex, 0, Items.Count - 1);
        if (from != to) Items.Move(from, to);
        _store.MoveInDock(id, to);
    }

    /// <summary>Jeux désinstallés ou sur un disque absent : relu à l'ouverture du dock et à chaque changement de profil.</summary>
    public async void RefreshInstallStates()
    {
        var items = Items.ToList();
        var paths = items.Select(i => i.ExePath).ToList();
        // Hors du thread UI : un lecteur réseau absent peut faire attendre File.Exists plusieurs secondes.
        var states = await Task.Run(() => paths.Select(p => GameInstallation.Of(p, File.Exists, Directory.Exists)).ToList());
        for (var i = 0; i < items.Count; i++) items[i].InstallState = states[i];
    }

    private void Reload()
    {
        var dock = _store.GetDock();
        if (dock.Count == Items.Count && dock.Zip(Items).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name
                && p.First.CoverImageId == p.Second.CoverImageId && p.First.ExePath == p.Second.ExePath))
        {
            RefreshInstallStates(); // rien de visible n'a changé : on garde les éléments (et les animations en cours)
            return;
        }

        var launching = Items.Where(i => i.IsLaunching).Select(i => i.Id).ToHashSet();
        Items.Clear();
        foreach (var profile in dock)
        {
            var item = new DockItemViewModel(profile)
            {
                CoverPath = _artwork.TryGetCached(profile.CoverImageId, Igdb.CoverSize),
                IsLaunching = launching.Contains(profile.Id),
            };
            Items.Add(item);
            if (item.CoverPath is null && profile.CoverImageId is not null)
            {
                _ = LoadCoverAsync(item, profile.CoverImageId);
            }
        }
        OnPropertyChanged(nameof(IsEmpty));
        RefreshInstallStates();
    }

    private async Task LoadCoverAsync(DockItemViewModel item, string imageId) =>
        item.CoverPath = await _artwork.GetAsync(imageId, Igdb.CoverSize);

    private void EndLaunchFeedback()
    {
        foreach (var item in Items) item.IsLaunching = false;
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}

public sealed partial class DockItemViewModel(GameProfile profile) : ObservableObject
{
    public Guid Id { get; } = profile.Id;

    public string Name { get; } = profile.Name;

    public string? CoverImageId { get; } = profile.CoverImageId;

    public string ExePath { get; } = profile.ExePath;

    public string Initials { get; } = string.Concat(profile.Name
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverPath;

    public bool HasCover => CoverPath is not null;

    /// <summary>Clic pris en compte : le jeu ou son lanceur s'ouvre (les clics suivants sont ignorés).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(ToolTipText))]
    private bool _isLaunching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnavailable), nameof(Status), nameof(ToolTipText))]
    private InstallState _installState = InstallState.Installed;

    /// <summary>Désinstallé ou disque absent : jaquette grisée (le clic explique pourquoi le jeu ne se lance pas).</summary>
    public bool IsUnavailable => InstallState != InstallState.Installed;

    /// <summary>État lu par les lecteurs d'écran et affiché dans l'info-bulle.</summary>
    public string Status => IsLaunching ? "Lancement…" : InstallState switch
    {
        InstallState.Uninstalled => "Désinstallé",
        InstallState.DriveUnavailable => "Disque absent",
        _ => "",
    };

    public string ToolTipText => Status.Length == 0 ? Name : $"{Name} — {Status}";
}
