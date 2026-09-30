using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Artwork;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Platform.Artwork;

namespace OptiGame.App.ViewModels;

/// <summary>« Mes jeux » : grille de jaquettes, puis page du jeu sélectionné.</summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly IPowerSchemeProvider _power;
    private readonly IRunningProgramsProvider _programs;
    private readonly IDialogService _dialogs;
    private readonly IGameLibraryScanner _scanner;
    private readonly AppSettingsStore _settings;
    private readonly IgdbClient _igdb;
    private readonly ArtworkCache _artwork;
    private readonly GameSessionManager _sessions;
    private readonly CaptureStore _captures;
    private readonly MeasuresViewModel _measures;
    private readonly NavigationService _navigation;
    private readonly TimeProvider _time;
    private readonly FileLog _log;

    /// <summary>Profils déjà cherchés sur IGDB pendant cette exécution (on ne redemande pas un jeu introuvable).</summary>
    private readonly HashSet<Guid> _artworkSearched = [];
    private IReadOnlyList<PowerScheme>? _schemes;
    private bool _fetchingArtwork;

    public LibraryViewModel(ProfileStore store, IPowerSchemeProvider power, IRunningProgramsProvider programs, IDialogService dialogs,
        IGameLibraryScanner scanner, AppSettingsStore settings, IgdbClient igdb, ArtworkCache artwork, GameSessionManager sessions,
        CaptureStore captures, MeasuresViewModel measures, NavigationService navigation, SettingsViewModel settingsPage,
        TimeProvider time, FileLog log)
    {
        _store = store;
        _power = power;
        _programs = programs;
        _dialogs = dialogs;
        _scanner = scanner;
        _settings = settings;
        _igdb = igdb;
        _artwork = artwork;
        _sessions = sessions;
        _captures = captures;
        _measures = measures;
        _navigation = navigation;
        _time = time;
        _log = log;

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = o => o is GameCardViewModel card &&
                                (string.IsNullOrWhiteSpace(SearchText) || card.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));

        store.ArtworkChanged += (_, id) => OnUi(() => _ = RefreshCoverAsync(id));
        sessions.SessionStarted += (_, _) => OnUi(RefreshPlaying);
        sessions.SessionEnded += (_, _) => OnUi(RefreshPlaying);
        settingsPage.IgdbCredentialsChanged += (_, _) =>
        {
            _artworkSearched.Clear();
            _ = FetchMissingArtworkAsync();
        };

        ReloadCards();
        _ = FetchMissingArtworkAsync();
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    public ICollectionView GamesView { get; }

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => GamesView.Refresh();

    /// <summary>Page du jeu ouverte ; null = grille.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridVisible))]
    private GamePageViewModel? _openGame;

    public bool IsGridVisible => OpenGame is null;

    public bool HasGames => Games.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanGamesCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _artworkStatus = "";

    public string Subtitle => Games.Count switch
    {
        0 => "Aucun jeu pour l'instant.",
        1 => "1 jeu",
        var n => $"{n} jeux",
    };

    private IReadOnlyList<PowerScheme> Schemes
    {
        get
        {
            try
            {
                return _schemes ??= _power.GetSchemes();
            }
            catch (Exception)
            {
                return _schemes = [];
            }
        }
    }

    // ---- Grille ----

    [RelayCommand]
    private void OpenCard(GameCardViewModel card) => OpenPage(card.Id);

    [RelayCommand]
    private void AddGame()
    {
        var exe = _dialogs.PickExecutable(null);
        if (exe is null) return;

        if (_store.GetAll().FirstOrDefault(p => p.Matches(exe)) is { } existing)
        {
            OpenPage(existing.Id);
            return;
        }

        var profile = new GameProfile { Name = DialogService.FriendlyName(exe), ExePath = exe };
        try
        {
            _store.Save(profile);
        }
        catch (ProfileValidationException ex)
        {
            _dialogs.ShowError(ex.Message);
            return;
        }
        ReloadCards();
        _ = FetchMissingArtworkAsync();
        OpenPage(profile.Id);
    }

    private bool CanScan() => !IsScanning;

    /// <summary>Recherche des jeux installés (Steam + dossiers de jeux), lecture seule.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanGamesAsync()
    {
        var folders = _settings.Get().GameFolders;
        IReadOnlyList<InstalledGame> games;
        IsScanning = true;
        try
        {
            games = await Task.Run(() => _scanner.Scan(folders));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("La recherche des jeux a échoué.\n\n" + ex.Message);
            return;
        }
        finally
        {
            IsScanning = false;
        }

        var existing = _store.GetAll();
        var selection = _dialogs.PickInstalledGames(games, path => existing.Any(p => p.Matches(path)), folders);
        var errors = new List<string>();
        foreach (var (game, exe) in selection)
        {
            try
            {
                _store.Save(new GameProfile { Name = CleanName(game.Name), ExePath = exe.Path });
            }
            catch (ProfileValidationException ex)
            {
                errors.Add($"{game.Name} : {ex.Message}");
            }
        }

        ReloadCards();
        _ = FetchMissingArtworkAsync();
        if (errors.Count > 0) _dialogs.ShowError("Certains jeux n'ont pas été ajoutés :\n\n" + string.Join("\n", errors));
    }

    // ---- Page du jeu ----

    private void OpenPage(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;

        var editor = new ProfileEditorViewModel(profile, Schemes, _programs, _dialogs, SaveProfileAsync, DeleteProfile, () => OpenPage(id));
        var exeName = Path.GetFileName(profile.ExePath);
        var captures = _captures.GetAll()
            .Where(c => c.ProcessName.Equals(exeName, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(c => new CaptureItemViewModel(c, _time, false))
            .ToList();

        OpenGame = new GamePageViewModel(profile, editor, captures,
            back: CloseGamePage,
            changeCover: () => ChangeCoverAsync(id),
            measure: () =>
            {
                _measures.SelectTarget(exeName);
                _navigation.Navigate(_measures);
            },
            isPlaying: _sessions.Current?.Profile.Id == id);
        _ = LoadPageImagesAsync(OpenGame, profile);
    }

    private void CloseGamePage()
    {
        if (OpenGame is { Editor.IsDirty: true } &&
            !_dialogs.Confirm("Les modifications de ce jeu ne sont pas enregistrées. Les abandonner ?"))
        {
            return;
        }
        OpenGame = null;
    }

    private Task SaveProfileAsync(GameProfile profile)
    {
        try
        {
            _store.Save(profile);
        }
        catch (ProfileValidationException ex)
        {
            _dialogs.ShowError("Le profil n'a pas été enregistré :\n\n" + ex.Message);
            return Task.CompletedTask;
        }
        ReloadCards();
        OpenPage(profile.Id); // Recharge la page depuis la version enregistrée (plus de modifications en attente).
        return Task.CompletedTask;
    }

    private void DeleteProfile(GameProfile profile)
    {
        if (!_dialogs.Confirm($"Retirer « {profile.Name} » de vos jeux ?\n\nSon profil est supprimé ; aucun réglage de Windows n'est modifié.")) return;
        _store.Remove(profile.Id);
        OpenGame = null;
        ReloadCards();
    }

    private async Task ChangeCoverAsync(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;
        if (!_igdb.IsConfigured)
        {
            _dialogs.ShowInfo("Pour choisir une jaquette, renseignez d'abord vos identifiants IGDB (Twitch) dans Paramètres.");
            return;
        }

        var choice = _dialogs.PickIgdbGame(profile.Name,
            query => _igdb.SearchAsync(query),
            imageId => _artwork.GetAsync(imageId, "cover_big"));
        if (choice is null) return;

        _store.SetArtwork(id, choice.Id, choice.CoverImageId, choice.HeroImageId);
        if (OpenGame?.Id == id && _store.Find(id) is { } updated)
        {
            await LoadPageImagesAsync(OpenGame, updated);
        }
    }

    // ---- Jaquettes ----

    /// <summary>Cherche sur IGDB les jaquettes manquantes, une requête à la fois, en arrière-plan.</summary>
    private async Task FetchMissingArtworkAsync()
    {
        if (_fetchingArtwork || !_igdb.IsConfigured) return;
        _fetchingArtwork = true;
        try
        {
            foreach (var profile in _store.GetAll().Where(p => p.CoverImageId is null && !_artworkSearched.Contains(p.Id)))
            {
                _artworkSearched.Add(profile.Id);
                ArtworkStatus = $"Recherche de la jaquette de {profile.Name}…";
                var results = await _igdb.SearchAsync(profile.Name);
                if (Igdb.BestMatch(profile.Name, results) is { } best)
                {
                    _store.SetArtwork(profile.Id, best.Id, best.CoverImageId, best.HeroImageId);
                }
                else
                {
                    _log.Info($"IGDB : aucune jaquette trouvée pour « {profile.Name} ».");
                }
            }
            ArtworkStatus = "";
        }
        catch (Exception ex)
        {
            _log.Error("Recherche des jaquettes IGDB interrompue", ex);
            ArtworkStatus = $"Jaquettes indisponibles : {ex.Message}";
        }
        finally
        {
            _fetchingArtwork = false;
        }
    }

    private void ReloadCards()
    {
        Games.Clear();
        var playing = _sessions.Current?.Profile.Id;
        foreach (var profile in _store.GetAll().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var card = new GameCardViewModel(profile, playing == profile.Id)
            {
                CoverPath = _artwork.TryGetCached(profile.CoverImageId, Igdb.CoverSize),
            };
            Games.Add(card);
            if (card.CoverPath is null && profile.CoverImageId is not null)
            {
                _ = RefreshCoverAsync(profile.Id);
            }
        }
        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(Subtitle));
    }

    private async Task RefreshCoverAsync(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;
        var path = await _artwork.GetAsync(profile.CoverImageId, Igdb.CoverSize);
        if (Games.FirstOrDefault(g => g.Id == id) is { } card) card.CoverPath = path;
        if (OpenGame?.Id == id) await LoadPageImagesAsync(OpenGame, profile);
    }

    private async Task LoadPageImagesAsync(GamePageViewModel page, GameProfile profile)
    {
        page.CoverPath = await _artwork.GetAsync(profile.CoverImageId, Igdb.CoverSize);
        page.HeroPath = await _artwork.GetAsync(profile.HeroImageId, Igdb.HeroSize);
    }

    private void RefreshPlaying()
    {
        var playing = _sessions.Current?.Profile.Id;
        foreach (var card in Games) card.IsPlaying = card.Id == playing;
        if (OpenGame is not null) OpenGame.IsPlaying = OpenGame.Id == playing;
    }

    private static string CleanName(string name) => name.Replace("®", "").Replace("™", "").Trim();

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}

/// <summary>Jaquette de la grille.</summary>
public sealed partial class GameCardViewModel(GameProfile profile, bool isPlaying) : ObservableObject
{
    public Guid Id { get; } = profile.Id;

    public string Name { get; } = profile.Name;

    public string ExeName { get; } = Path.GetFileName(profile.ExePath);

    public bool Enabled { get; } = profile.Enabled;

    /// <summary>Initiales affichées tant qu'il n'y a pas de jaquette.</summary>
    public string Initials { get; } = string.Concat(profile.Name
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(w => char.IsLetterOrDigit(w[0]))
        .Take(2)
        .Select(w => char.ToUpperInvariant(w[0])));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverPath;

    public bool HasCover => CoverPath is not null;

    [ObservableProperty]
    private bool _isPlaying = isPlaying;
}
