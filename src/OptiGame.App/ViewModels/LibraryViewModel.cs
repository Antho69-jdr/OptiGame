using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Services;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Artwork;
using OptiGame.Core.Launching;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;
using OptiGame.Core.Playtime;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Core.Text;
using OptiGame.Platform.Artwork;
using OptiGame.Platform.Library;
using OptiGame.Platform.Processes;

namespace OptiGame.App.ViewModels;

/// <summary>« Mes jeux » : grille de jaquettes, puis page du jeu sélectionné.</summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly GameTagService _tags;
    private readonly Platform.Library.StorePageResolver _storePages;
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
    private readonly GameLauncher _launcher;
    private readonly PlaytimeStore _playtime;
    private readonly SteamPlaytimeReader _steamReader;
    private readonly GameGraphicsService _graphics;
    private readonly FrameCapService _frameCap;
    private readonly Platform.Library.StoreOwnedLibrary _storeLibrary;
    private readonly Platform.Library.StoreCoverCache _storeCovers;
    private readonly GameRatingService _ratings;
    private readonly GameTimeGate _gate;
    private readonly Core.State.ChangeJournal _fixes;
    private int _ratingVersion;

    /// <summary>Notes calculées au moins une fois (avant : la fiche dit « calcul en cours », pas « pas de note »).</summary>
    private bool _ratingsLoaded;

    /// <summary>Dernier onglet choisi dans une fiche : la suivante s'ouvre sur le même.</summary>
    private GameTab _lastGameTab = GameTab.Overview;

    private readonly SessionViewModel _session;

    /// <summary>Temps de jeu Steam par profil (jeux Steam), relu à chaque changement de la bibliothèque ou de Steam.</summary>
    private IReadOnlyDictionary<Guid, SteamPlaytimeEntry> _steamPlaytime = new Dictionary<Guid, SteamPlaytimeEntry>();
    private int _steamReadVersion;

    /// <summary>Profils déjà cherchés sur IGDB pendant cette exécution (on ne redemande pas un jeu introuvable).</summary>
    private readonly HashSet<Guid> _artworkSearched = [];
    private IReadOnlyList<PowerScheme>? _schemes;
    private bool _fetchingArtwork;

    public LibraryViewModel(ProfileStore store, IPowerSchemeProvider power, IRunningProgramsProvider programs, IDialogService dialogs,
        IGameLibraryScanner scanner, AppSettingsStore settings, IgdbClient igdb, ArtworkCache artwork, GameSessionManager sessions,
        CaptureStore captures, MeasuresViewModel measures, NavigationService navigation, SettingsViewModel settingsPage,
        TimeProvider time, FileLog log, GameLauncher launcher, PlaytimeStore playtime, SteamPlaytimeReader steamReader,
        GameGraphicsService graphics, NewSteamGamesViewModel newGames, GameRatingService ratings, Platform.Measurement.AutoCapture autoCapture,
        FrameCapService frameCap, Platform.Library.StoreOwnedLibrary storeLibrary, Platform.Library.StoreCoverCache storeCovers,
        GameTimeGate gate, [FromKeyedServices(Platform.JournalKeys.Fixes)] Core.State.ChangeJournal fixes, UnsavedChangesGuard unsaved,
        SessionViewModel session, GameTagService tags, Platform.Library.StorePageResolver storePages)
    {
        _session = session;
        unsaved.Register(ConfirmDiscard);
        // Dock activé ou non (Paramètres) : épingles des jaquettes affichées seulement s'il l'est.
        settings.Changed += (_, _) => OnUi(() => OnPropertyChanged(nameof(IsDockEnabled)));
        _gate = gate;
        _fixes = fixes;
        _steamReader = steamReader;
        _graphics = graphics;
        _frameCap = frameCap;
        _storeLibrary = storeLibrary;
        _storeCovers = storeCovers;
        _ratings = ratings;
        // Nouvelle mesure (pendant la partie) : la note change, recalculée à la fin de la partie.
        autoCapture.CaptureAdded += (_, _) => OnUi(() => _gate.RunOrDefer("ratings", () => _ = LoadRatingsAsync()));
        NewGames = newGames;
        newGames.GameAdded += (_, _) =>
        {
            ReloadCards();
            _ = FetchMissingArtworkAsync();
        };
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
        _launcher = launcher;
        _playtime = playtime;

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = o => o is GameCardViewModel card && Matches(card.Name, card.Store, card.Genres, card.Kinds);
        _tags = tags;
        _storePages = storePages;
        tags.Updated += (_, _) => ApplyTags(rebuildFilters: true); // genres IGDB arrivés en tâche de fond
        _selectedGenre = GenreOptions[0];
        _selectedKind = KindOptions[0];
        _selectedStore = StoreOptions[0];
        // Un jeu vient d'être installé (Steam télécharge parfois pendant une partie) : relu à la fin de la partie.
        newGames.Proposals.CollectionChanged += (_, _) => _gate.RunOrDefer("libraries", QueueLibraryLoad);

        store.ArtworkChanged += (_, id) => OnUi(() => _ = RefreshCoverAsync(id));
        playtime.Changed += (_, _) => OnUi(RefreshPlaytime);
        // Steam a écrit le temps d'une partie (localconfig.vdf, parfois plusieurs Mo) : relu hors partie.
        steamReader.Changed += (_, _) => OnUi(() => _gate.RunOrDefer("steam-playtime", () => _ = LoadSteamPlaytimeAsync()));
        store.DockChanged += (_, _) => OnUi(() =>
        {
            if (OpenGame is not null) OpenGame.IsPinned = _store.Find(OpenGame.Id)?.DockOrder is not null;
            foreach (var card in Games) card.IsPinned = _store.Find(card.Id)?.DockOrder is not null;
        });
        ApplySort();
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

    /// <summary>Nouveaux jeux Steam installés, proposés en haut de la grille.</summary>
    public NewSteamGamesViewModel NewGames { get; }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    public ICollectionView GamesView { get; }

    [ObservableProperty]
    private string _searchText = "";

    public IReadOnlyList<string> SortOptions { get; } = ["Récemment joués", "Temps de jeu", "Nom"];

    [ObservableProperty]
    private string _selectedSort = "Récemment joués";

    partial void OnSelectedSortChanged(string value) => ApplySort();

    private void ApplySort()
    {
        using (GamesView.DeferRefresh())
        {
            GamesView.SortDescriptions.Clear();
            if (SelectedSort == "Récemment joués")
            {
                GamesView.SortDescriptions.Add(new SortDescription(nameof(GameCardViewModel.LastPlayedTicks), ListSortDirection.Descending));
            }
            else if (SelectedSort == "Temps de jeu")
            {
                GamesView.SortDescriptions.Add(new SortDescription(nameof(GameCardViewModel.TotalTicks), ListSortDirection.Descending));
            }
            GamesView.SortDescriptions.Add(new SortDescription(nameof(GameCardViewModel.Name), ListSortDirection.Ascending));
        }
    }

    /// <summary>Temps de jeu modifié (début/fin de partie, relecture de Steam) : jaquettes, page ouverte et tri.</summary>
    private void RefreshPlaytime()
    {
        var now = _time.GetLocalNow();
        foreach (var card in Games) card.Playtime = PlaytimeOf(card.Id, now);
        if (OpenGame is not null) RefreshPagePlaytime(OpenGame);
        GamesView.Refresh();
    }

    private void RefreshPagePlaytime(GamePageViewModel page)
    {
        var now = _time.GetLocalNow();
        page.SetPlaytime(PlaytimeOf(page.Id, now), _playtime.RecentSessions(page.Id, 5), now);
    }

    /// <summary>Suivi d'OptiGame complété par le temps Steam (rétroactif) pour les jeux Steam.</summary>
    private PlaytimeSummary PlaytimeOf(Guid id, DateTimeOffset now) =>
        PlaytimeSummary.Combine(_playtime.StatsFor(id, now), _steamPlaytime.GetValueOrDefault(id));

    /// <summary>
    /// Notes des jeux (configuration requise Steam en cache, captures), hors du thread UI ; la lecture la plus récente
    /// l'emporte. Une note introuvable laisse simplement la jaquette sans pastille.
    /// </summary>
    private async Task LoadRatingsAsync()
    {
        var version = ++_ratingVersion;
        var profiles = _store.GetAll();
        var ratings = await Task.Run(async () =>
        {
            var apps = Platform.Library.GameLibraryScanner.SteamApps();
            var result = new Dictionary<Guid, Core.Rating.GameRating?>();
            foreach (var profile in profiles)
            {
                try
                {
                    result[profile.Id] = await _ratings.RateAsync(profile, apps);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Management.ManagementException or InvalidOperationException)
                {
                    _log.Error($"Note de « {profile.Name} » impossible", ex);
                }
            }
            return result;
        });
        if (version != _ratingVersion) return;
        _ratingsLoaded = true;
        foreach (var card in Games) card.Rating = ratings.GetValueOrDefault(card.Id);
        if (OpenGame is not null)
        {
            OpenGame.Rating = ratings.GetValueOrDefault(OpenGame.Id);
            OpenGame.IsRatingLoaded = true;
        }
    }

    /// <summary>Relit le temps Steam (localconfig.vdf + manifestes, hors du thread UI) ; la lecture la plus récente l'emporte.</summary>
    private async Task LoadSteamPlaytimeAsync()
    {
        var version = ++_steamReadVersion;
        var profiles = _store.GetAll();
        var steam = await Task.Run(() => _steamReader.Read(profiles));
        if (version != _steamReadVersion) return;
        _steamPlaytime = steam;
        RefreshPlaytime();
    }

    private int _searchVersion;

    partial void OnSearchTextChanged(string value) => _ = RefreshFiltersAfterTypingAsync();

    private async Task RefreshFiltersAfterTypingAsync()
    {
        var version = ++_searchVersion;
        await Task.Delay(200);
        if (version == _searchVersion) RefreshFilters();
    }

    // ---- Filtres (nom, magasin, genre, type) et jeux possédés non installés ----

    /// <summary>Genres présents dans les bibliothèques (noms français du magasin Steam, aussi pour les autres magasins).</summary>
    public ObservableCollection<GenreOption> GenreOptions { get; } = [new(null, "Tous les genres")];

    [ObservableProperty]
    private GenreOption _selectedGenre;

    partial void OnSelectedGenreChanged(GenreOption value)
    {
        RefreshFilters();
        OnPropertyChanged(nameof(ShowsTagsHint));
    }

    public IReadOnlyList<KindOption> KindOptions { get; } =
    [
        new(null, "Tous les types"),
        .. Enum.GetValues<Core.Library.GameKind>().Select(k => new KindOption(k, Core.Library.SteamTaxonomy.Label(k))),
    ];

    [ObservableProperty]
    private KindOption _selectedKind;

    partial void OnSelectedKindChanged(KindOption value)
    {
        RefreshFilters();
        OnPropertyChanged(nameof(ShowsTagsHint));
    }

    public IReadOnlyList<StoreOption> StoreOptions { get; } =
    [
        new(null, "Tous les magasins"),
        .. new[] { GameSource.Steam, GameSource.Epic, GameSource.Gog }
            .Select(s => new StoreOption(s, Core.Library.StoreCatalogs.Label(s))),
    ];

    [ObservableProperty]
    private StoreOption _selectedStore;

    partial void OnSelectedStoreChanged(StoreOption value) => RefreshFilters();

    /// <summary>Un jeu sans genre, type ou magasin connu disparaît seulement quand le filtre correspondant est choisi.</summary>
    private bool Matches(string name, GameSource? store, IReadOnlyList<string> genres, IReadOnlySet<Core.Library.GameKind> kinds) =>
        (string.IsNullOrWhiteSpace(SearchText) || name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
        (SelectedStore?.Store is not { } wantedStore || store == wantedStore) &&
        (SelectedGenre?.Name is not { } genre || genres.Contains(genre)) &&
        (SelectedKind?.Kind is not { } kind || kinds.Contains(kind));

    /// <summary>Vrai pendant la reconstruction de la liste des genres (relecture des bibliothèques) : un seul rafraîchissement, à la fin.</summary>
    private bool _rebuildingFilterOptions;

    private void RefreshFilters()
    {
        if (_rebuildingFilterOptions) return;
        ApplyFilters(keepShownCount: false);
    }

    /// <summary>
    /// Filtres appliqués aux deux grilles. Jeux non installés : filtre changé → première page, défilement en haut ; relecture des
    /// bibliothèques (<paramref name="keepShownCount"/>) → autant de jeux qu'avant, pour que le défilement ne saute pas.
    /// </summary>
    private void ApplyFilters(bool keepShownCount)
    {
        _searchVersion++; // une recherche en attente est appliquée maintenant
        GamesView.Refresh();
        ShowUninstalledList(keepShownCount);
        OnPropertyChanged(nameof(IsFiltered));
        NotifyCounts();
    }

    /// <summary>Jeux de « Mes jeux » retenus par la recherche et les filtres.</summary>
    public int VisibleGamesCount => GamesView.Cast<object>().Count();

    public bool HasVisibleGames => VisibleGamesCount > 0;

    /// <summary>Jeux non installés retenus par la recherche et les filtres (la section se masque s'il n'y en a aucun).</summary>
    public bool HasVisibleUninstalled => _uninstalledFiltered.Count > 0;

    /// <summary>Recherche ou filtres actifs et rien à montrer (ni jeu installé, ni non installé affiché).</summary>
    public bool HasNoResults => IsFiltered && (Games.Count > 0 || IsUninstalledSectionVisible) && VisibleGamesCount == 0 &&
                                (!IsUninstalledSectionVisible || _uninstalledFiltered.Count == 0);

    public string NoResultsText => string.IsNullOrWhiteSpace(SearchText)
        ? "Aucun jeu ne correspond à ces filtres"
        : $"Aucun jeu ne correspond à « {SearchText.Trim()} »";

    /// <summary>En-tête des jeux installés, quand la section des non installés est affichée aussi.</summary>
    public string InstalledHeader => $"Installés · {VisibleGamesCount}";

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(VisibleGamesCount));
        OnPropertyChanged(nameof(HasVisibleGames));
        OnPropertyChanged(nameof(HasVisibleUninstalled));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(NoResultsText));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(InstalledHeader));
        OnPropertyChanged(nameof(UninstalledHeader));
    }

    public bool IsFiltered => SelectedGenre?.Name is not null || SelectedKind?.Kind is not null || SelectedStore?.Store is not null ||
                              !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        SelectedGenre = GenreOptions[0];
        SelectedKind = KindOptions[0];
        SelectedStore = StoreOptions[0];
    }

    public ObservableCollection<OwnedGameCardViewModel> UninstalledGames { get; } = [];


    /// <summary>Afficher les jeux possédés mais non installés (mémorisé) ; leurs jaquettes Epic et GOG sont alors téléchargées.</summary>
    public bool ShowUninstalled
    {
        get => _settings.Get().LibraryShowUninstalled;
        set
        {
            if (value == ShowUninstalled) return;
            _settings.Update(s => s.LibraryShowUninstalled = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUninstalledSectionVisible));
            NotifyCounts();
            if (value) _ = FetchStoreCoversAsync();
            else TrimUninstalledToFirstPage(); // section masquée : ses jaquettes déjà chargées sont libérées
        }
    }

    public bool HasUninstalled => UninstalledGames.Count > 0;

    /// <summary>Pour la mesure de la mémoire (journal) : jaquettes réellement affichées, et images décodées.</summary>
    public string DescribeForMemoryReport() =>
        $"cartes : {Games.Count} jeux de « Mes jeux », {VisibleUninstalled.Count} non installés affichés sur {UninstalledGames.Count} " +
        $"(section {(IsUninstalledSectionVisible ? "affichée" : "masquée")}) · {Converters.ImageLoader.Describe()}";

    public bool IsUninstalledSectionVisible => ShowUninstalled && HasUninstalled;

    public string UninstalledToggleText => $"Afficher les jeux non installés ({UninstalledGames.Count})";

    /// <summary>En-tête de la section : jeux possédés dans les bibliothèques des magasins, non installés.</summary>
    public string UninstalledHeader => $"Non installés · {_uninstalledFiltered.Count}";

    private const int UninstalledPageSize = 48;

    /// <summary>Jeux non installés retenus par les filtres, dans l'ordre affiché.</summary>
    private List<OwnedGameCardViewModel> _uninstalledFiltered = [];

    /// <summary>
    /// Jeux non installés réellement affichés : une page de plus à chaque fois qu'on approche du bas (LibraryView) ou qu'on clique
    /// « Afficher plus ». WPF n'a pas de WrapPanel virtualisé : sans pages, les ~600 jaquettes seraient créées d'un coup.
    /// </summary>
    public ObservableCollection<OwnedGameCardViewModel> VisibleUninstalled { get; } = [];

    public bool HasMoreUninstalled => VisibleUninstalled.Count < _uninstalledFiltered.Count;

    public string ShowMoreUninstalledText => $"Afficher plus ({_uninstalledFiltered.Count - VisibleUninstalled.Count} restants)";

    /// <summary>La liste des jeux non installés est revenue à sa première page : LibraryView remonte le défilement en haut.</summary>
    public event EventHandler? UninstalledListReset;

    private void ShowUninstalledList(bool keepShownCount)
    {
        var shown = keepShownCount ? Math.Max(VisibleUninstalled.Count, UninstalledPageSize) : UninstalledPageSize;
        // Simple liste filtrée et triée (pas de vue WPF : elle n'est affichée que par pages, via VisibleUninstalled).
        _uninstalledFiltered = UninstalledGames
            .Where(game => Matches(game.Name, game.Store, game.Genres, game.Kinds))
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        VisibleUninstalled.Clear();
        foreach (var card in _uninstalledFiltered.Take(shown)) VisibleUninstalled.Add(card);
        NotifyUninstalledPaging();
        if (!keepShownCount) UninstalledListReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Retour à la première page SANS recréer ses cartes (ni redécoder leurs jaquettes), défilement en haut.</summary>
    private void TrimUninstalledToFirstPage()
    {
        while (VisibleUninstalled.Count > UninstalledPageSize) VisibleUninstalled.RemoveAt(VisibleUninstalled.Count - 1);
        NotifyUninstalledPaging();
        UninstalledListReset?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyUninstalledPaging()
    {
        OnPropertyChanged(nameof(HasMoreUninstalled));
        OnPropertyChanged(nameof(ShowMoreUninstalledText));
        OnPropertyChanged(nameof(UninstalledHeader));
    }

    /// <summary>
    /// Fenêtre masquée (l'appli reste dans la zone de notification, souvent pendant une partie) : les jeux non installés
    /// reviennent à leur première page et le cache d'images est vidé, pour que les centaines de jaquettes affichées
    /// (≈ 1 Mo chacune mesuré) ne restent pas en mémoire.
    /// </summary>
    public void ReleaseCovers()
    {
        if (VisibleUninstalled.Count > UninstalledPageSize) TrimUninstalledToFirstPage();
        Converters.ImageLoader.Clear();
    }

    [RelayCommand]
    private void ShowMoreUninstalled()
    {
        foreach (var card in _uninstalledFiltered.Skip(VisibleUninstalled.Count).Take(UninstalledPageSize)) VisibleUninstalled.Add(card);
        NotifyUninstalledPaging();
    }

    private int _libraryVersion;
    private bool _libraryLoadQueued;

    /// <summary>Regroupe les demandes rapprochées (une proposition retirée puis d'autres ajoutées…) en une seule relecture.</summary>
    private async void QueueLibraryLoad()
    {
        if (_libraryLoadQueued) return;
        _libraryLoadQueued = true;
        await Task.Delay(300);
        _libraryLoadQueued = false;
        _libraryLoad = Logged(LoadLibrariesAsync(), "Lecture des bibliothèques des magasins");
    }

    /// <summary>
    /// Tâche lancée sans être attendue : une erreur inattendue serait perdue sans bruit (ex. section « non installés » vide).
    /// Elle est journalisée ; la tâche rendue ne lève jamais, pour qu'« Actualiser » puisse l'attendre.
    /// </summary>
    private async Task Logged(Task task, string what)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            _log.Error($"{what} : échec inattendu", ex);
        }
    }

    /// <summary>
    /// Bibliothèques des magasins, hors du thread UI : Steam (caches du client : genres et types des jeux installés, jeux possédés
    /// non installés) puis Epic et GOG (catalogue d'Epic, copie de la base de GOG Galaxy). Une source illisible est
    /// simplement ignorée (journalisée).
    /// </summary>
    private async Task LoadLibrariesAsync()
    {
        var version = ++_libraryVersion;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var profiles = _store.GetAll();
        Platform.Library.SteamOwnedSnapshot? snapshot = null;
        Dictionary<Guid, uint> profileApps = [];
        List<OwnedGameCardViewModel> uninstalled = [];
        try
        {
            (snapshot, profileApps, uninstalled) = await Task.Run(() =>
            {
                Platform.Library.SteamOwnedSnapshot? owned = null;
                try
                {
                    owned = Platform.Library.SteamOwnedLibrary.Read();
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
                {
                    _log.Warn($"Bibliothèque Steam illisible (caches du client) : {ex.Message}");
                }
                var apps = Platform.Library.GameLibraryScanner.SteamApps();
                var installed = apps.Select(a => uint.TryParse(a.AppId, out var id) ? id : 0).ToHashSet();
                var byProfile = profiles
                    .Select(p => (p.Id, AppId: Platform.Library.GameLibraryScanner.SteamAppIdFor(p, apps)))
                    .Where(x => uint.TryParse(x.AppId, out _))
                    .ToDictionary(x => x.Id, x => uint.Parse(x.AppId!, System.Globalization.CultureInfo.InvariantCulture));
                var profileNames = profiles.Select(p => Core.Library.StoreCatalogs.NameKey(p.Name)).ToHashSet();

                var cards = owned?.Games.Values
                    .Where(g => !installed.Contains(g.AppId) && !byProfile.ContainsValue(g.AppId))
                    .Select(g => OwnedGameCardViewModel.FromSteam(g, Platform.Library.SteamOwnedLibrary.CoverPath(g.AppId)))
                    .ToList() ?? [];
                cards.AddRange(_storeLibrary.ReadNotInstalled()
                    .Where(g => !profileNames.Contains(Core.Library.StoreCatalogs.NameKey(g.Name))) // déjà dans « Mes jeux »
                    .Select(g => OwnedGameCardViewModel.FromStore(g, _storeCovers.TryGetCached(g.CoverUrl))));
                return (owned, byProfile, cards);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Warn($"Bibliothèques des magasins illisibles : {ex.Message}");
            return;
        }
        if (version != _libraryVersion) return;
        _log.Info($"Bibliothèques des magasins lues en {watch.ElapsedMilliseconds} ms ({uninstalled.Count} jeux non installés) — " +
                  $"mémoire : {MemoryUsage.Now().Describe()}");

        foreach (var card in Games)
        {
            var steamApp = profileApps.TryGetValue(card.Id, out var appId) ? appId : (uint?)null;
            card.Store = _store.Find(card.Id) is { } profile ? Core.Library.StoreCatalogs.StoreOf(profile, steamApp is not null) : null;
            card.StoreTags = steamApp is { } id && snapshot?.Games.TryGetValue(id, out var game) == true
                ? new Core.Library.GameTags(game.Genres.Select(Core.Library.SteamTaxonomy.Genre).OfType<string>().ToList(), Core.Library.SteamTaxonomy.Kinds(game.Categories))
                : Core.Library.GameTags.Empty;
        }
        UninstalledGames.Clear();
        foreach (var card in uninstalled)
        {
            card.Owner = this;
            UninstalledGames.Add(card);
        }
        ApplyTags(rebuildFilters: false);
        // Genres IGDB des jeux qui n'en ont pas encore (tous magasins), en tâche de fond : ApplyTags à leur arrivée.
        _tags.Request(Games.Select(c => c.Name).Concat(UninstalledGames.Select(c => c.Name)));

        RebuildGenreOptions();

        OnPropertyChanged(nameof(HasUninstalled));
        OnPropertyChanged(nameof(IsUninstalledSectionVisible));
        OnPropertyChanged(nameof(UninstalledToggleText));
        NotifyCounts();
        ApplyFilters(keepShownCount: true);
        if (ShowUninstalled) _ = FetchStoreCoversAsync();
    }

    /// <summary>
    /// Genres et types de chaque carte = ceux de son magasin (Steam, GOG Galaxy) + ceux d'IGDB (cache taxonomy.json) : un même
    /// vocabulaire pour tous les magasins (Core/Library/GameTaxonomy).
    /// </summary>
    private void ApplyTags(bool rebuildFilters)
    {
        foreach (var card in Games)
        {
            var tags = card.StoreTags.With(_tags.Find(card.Name));
            (card.Genres, card.Kinds) = (tags.Genres, tags.Kinds);
        }
        foreach (var card in UninstalledGames) card.ApplyTags(_tags.Find(card.Name));
        OnPropertyChanged(nameof(ShowsTagsHint));
        if (!rebuildFilters) return;
        RebuildGenreOptions();
        ApplyFilters(keepShownCount: true);
    }

    /// <summary>
    /// Filtre par genre ou type sans identifiants IGDB alors que des jeux n'ont ni genre ni type (Epic, jeux ajoutés à la main) :
    /// dit pourquoi ils n'apparaissent pas.
    /// </summary>
    public bool ShowsTagsHint =>
        (SelectedGenre?.Name is not null || SelectedKind?.Kind is not null) && !_tags.IsAvailable &&
        (Games.Any(c => c.Genres.Count == 0 && c.Kinds.Count == 0) || UninstalledGames.Any(c => c.Genres.Count == 0 && c.Kinds.Count == 0));

    private void RebuildGenreOptions()
    {
        var selected = SelectedGenre?.Name;
        var genres = Games.SelectMany(c => c.Genres).Concat(UninstalledGames.SelectMany(c => c.Genres))
            .Distinct()
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        // Vider la liste fait remettre le genre choisi à null par la liste déroulante, puis il est rétabli : sans ce drapeau, deux
        // rafraîchissements complets (et un retour à la première page) avant celui de la fin.
        _rebuildingFilterOptions = true;
        try
        {
            GenreOptions.Clear();
            GenreOptions.Add(new GenreOption(null, "Tous les genres"));
            foreach (var genre in genres) GenreOptions.Add(new GenreOption(genre, genre));
            SelectedGenre = GenreOptions.FirstOrDefault(o => o.Name == selected) ?? GenreOptions[0];
        }
        finally
        {
            _rebuildingFilterOptions = false;
        }
    }

    private bool _fetchingStoreCovers;

    /// <summary>
    /// Jaquettes Epic et GOG manquantes, téléchargées une fois quand la section est affichée, par lots de 4. Chaque lot est choisi
    /// dans l'ordre de la grille ACTUELLE (filtre et tri) : changer de filtre fait passer ses jaquettes en premier.
    /// </summary>
    private async Task FetchStoreCoversAsync()
    {
        if (_fetchingStoreCovers) return;
        _fetchingStoreCovers = true;
        var tried = new HashSet<OwnedGameCardViewModel>();
        try
        {
            while (true)
            {
                if (_gate.InGame)
                {
                    _gate.RunOrDefer("store-covers", () => { if (ShowUninstalled) _ = FetchStoreCoversAsync(); }); // la suite à la fin de la partie
                    break;
                }
                var batch = VisibleUninstalled
                    .Concat(_uninstalledFiltered)
                    .Concat(UninstalledGames)
                    .Where(c => c.CoverPath is null && c.CoverUrl is not null && !tried.Contains(c))
                    .Distinct()
                    .Take(4)
                    .ToList();
                if (batch.Count == 0) break;
                tried.UnionWith(batch);
                var paths = await Task.WhenAll(batch.Select(card => _storeCovers.GetAsync(card.CoverUrl)));
                for (var i = 0; i < batch.Count; i++)
                {
                    if (paths[i] is { } path) batch[i].CoverPath = path;
                }
            }
        }
        finally
        {
            _fetchingStoreCovers = false;
        }
    }

    [RelayCommand]
    private void InstallOwned(OwnedGameCardViewModel game)
    {
        try
        {
            ShowLaunchStatus(game.Store == GameSource.Steam
                ? InstallSteam(game)
                : _launcher.InstallStoreGame(game.Store, game.Key, game.Name));
        }
        catch (Exception ex) when (ex is LaunchException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            _log.Error($"Installation de « {game.Name} » impossible", ex);
            _dialogs.ShowError("Installation impossible",
                ex is LaunchException ? ex.Message : $"Le lanceur n'a pas pu ouvrir l'installation de « {game.Name} ». Vérifiez qu'il est installé, puis réessayez.",
                ex.Message);
        }
    }

    private string InstallSteam(OwnedGameCardViewModel game)
    {
        _launcher.InstallSteamGame(game.Key);
        return $"Steam ouvre l'installation de « {game.Name} ». Une fois installé, il vous sera proposé ici.";
    }

    [RelayCommand]
    private async Task OpenOwnedStorePageAsync(OwnedGameCardViewModel game)
    {
        if (game.Store == GameSource.Steam) OpenStorePage(game.Key);
        else if (game.Product is { } product) await OpenProductPageAsync(product);
    }

    /// <summary>« Voir sur Epic Games / GOG » : page exacte si le magasin la donne, sinon recherche du titre ; navigateur, sans admin.</summary>
    private async Task OpenProductPageAsync(Core.Library.StoreProduct product)
    {
        var url = await _storePages.UrlAsync(product);
        try
        {
            _launcher.OpenStoreWebPage(url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            _log.Error($"Page {product.Store} de « {product.Title} » non ouverte", ex);
            _dialogs.ShowError($"Impossible de voir {product.Title} sur {Core.Library.StoreCatalogs.Label(product.Store)}",
                "Le navigateur n'a pas pu ouvrir la page du magasin.", ex.Message);
        }
    }

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

    /// <summary>« 42 jeux », ou « 3 sur 42 jeux » quand une recherche ou un filtre est actif.</summary>
    public string Subtitle => Games.Count == 0
        ? "Aucun jeu pour l'instant"
        : IsFiltered ? $"{VisibleGamesCount} sur {FrenchText.Count(Games.Count, "jeu", "jeux")}" : FrenchText.Count(Games.Count, "jeu", "jeux");

    /// <summary>Le dock est affiché : l'épingle des jaquettes n'a de sens qu'alors.</summary>
    public bool IsDockEnabled => _settings.Get().DockEnabled;

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
    private Task PlayCard(GameCardViewModel card) => PlayAsync(card.Id);

    [RelayCommand]
    private void TogglePinCard(GameCardViewModel card) => _store.SetPinned(card.Id, !card.IsPinned);

    [RelayCommand]
    private void RemoveCard(GameCardViewModel card) => RemoveGame(card.Id);

    /// <summary>Message temporaire (lancement en cours, bibliothèque actualisée…), en InfoBar en haut de la grille et de la fiche.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLaunchStatus))]
    private string _launchStatus = "";

    public bool HasLaunchStatus => LaunchStatus.Length > 0;

    [ObservableProperty]
    private Controls.Severity _launchStatusSeverity = Controls.Severity.Info;

    [RelayCommand]
    private void DismissLaunchStatus() => LaunchStatus = "";

    /// <summary>Lance le jeu sans droits administrateur ; le profil s'appliquera par la détection habituelle. Faux si rien n'a été lancé.</summary>
    public async Task<bool> PlayAsync(Guid id)
    {
        if (_store.Find(id) is not { } profile) return false;
        if (OpenGame is { Editor.IsDirty: true } page && page.Id == id &&
            !_dialogs.Confirm("Jouer sans enregistrer ?",
                "Vos dernières modifications de ce jeu ne sont pas enregistrées : la partie utilisera les réglages enregistrés.",
                "Jouer sans enregistrer"))
        {
            return false;
        }
        if (_sessions.Current?.Profile.Id == id)
        {
            _dialogs.ShowInfo($"{profile.Name} est déjà en cours", "La partie est déjà lancée : basculez sur la fenêtre du jeu.");
            return false;
        }

        switch (GameInstallation.Of(profile.ExePath, File.Exists, Directory.Exists))
        {
            case InstallState.Uninstalled:
                _dialogs.ShowInfo($"{profile.Name} n'est plus installé",
                    $"Son fichier .exe est introuvable :\n{profile.ExePath}\n\nRéinstallez-le depuis son lanceur, ou retirez-le de Mes jeux.");
                return false;
            case InstallState.DriveUnavailable:
                _dialogs.ShowInfo("Disque absent",
                    $"Le disque de {profile.Name} ({GameInstallation.RootOf(profile.ExePath)}) n'est pas disponible. Branchez-le, puis réessayez.");
                return false;
        }

        try
        {
            var plan = await Task.Run(() => _launcher.Launch(profile));
            ShowLaunchStatus($"Lancement de {profile.Name} — {plan.Description}…");
            return true;
        }
        catch (Exception ex) when (ex is LaunchException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Error($"Lancement de « {profile.Name} » impossible", ex);
            _dialogs.ShowError($"Impossible de lancer {profile.Name}",
                ex is LaunchException ? ex.Message : "Le jeu ou son lanceur n'a pas pu être ouvert. Vérifiez qu'il est bien installé, puis réessayez.",
                ex.Message);
            return false;
        }
    }

    private async void ShowLaunchStatus(string text, Controls.Severity severity = Controls.Severity.Info)
    {
        LaunchStatusSeverity = severity;
        LaunchStatus = text;
        await Task.Delay(TimeSpan.FromSeconds(12));
        if (LaunchStatus == text) LaunchStatus = "";
    }

    /// <summary>Aperçu du lancement pour l'éditeur (lecture des manifestes Steam, quelques petits fichiers).</summary>
    private string DescribeLaunch(GameProfile profile)
    {
        try
        {
            var plan = _launcher.Plan(profile);
            return $"{plan.Description}\n{plan.CommandLine}";
        }
        catch (LaunchException ex)
        {
            return "⚠ " + ex.Message;
        }
    }

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
            _dialogs.ShowError("Jeu non ajouté", ex.Message);
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
            _dialogs.ShowError("Détection impossible", "OptiGame n'a pas pu parcourir vos bibliothèques et vos dossiers de jeux.", ex.Message);
            return;
        }
        finally
        {
            IsScanning = false;
        }

        var existing = _store.GetAll();
        bool HasProfile(string path) => existing.Any(p => p.Matches(path));
        // Rien de nouveau : pas de liste vide à fermer, un message dit pourquoi.
        if (!games.Any(g => !g.Candidates.Any(c => HasProfile(c.Path))))
        {
            _dialogs.ShowInfo(games.Count == 0 ? "Aucun jeu détecté" : "Aucun nouveau jeu",
                (games.Count == 0
                    ? "Aucun jeu installé n'a été trouvé dans Steam, Epic Games, GOG ni dans vos dossiers de jeux."
                    : games.Count == 1 ? "Le jeu détecté est déjà dans Mes jeux." : $"Les {games.Count} jeux détectés sont déjà dans Mes jeux.") +
                "\n\nUn jeu installé ailleurs s'ajoute avec « Ajouter des jeux », puis « Choisir un fichier .exe… », ou en ajoutant son dossier dans Paramètres.");
            return;
        }
        var selection = _dialogs.PickInstalledGames(games, HasProfile, folders);
        var errors = new List<string>();
        foreach (var (game, exe) in selection)
        {
            try
            {
                _store.Save(new GameProfile
                {
                    Name = CleanName(game.Name),
                    ExePath = exe.Path,
                    SteamAppId = game.SteamAppId,
                    // Jeux Epic et GOG : lancés par leur lanceur, comme leurs raccourcis (sinon l'exe ou Steam).
                    LaunchMode = game.LauncherPath is null ? LaunchMode.Automatic : LaunchMode.Launcher,
                    LauncherPath = game.LauncherPath,
                    LaunchArguments = game.LaunchArguments,
                });
            }
            catch (ProfileValidationException ex)
            {
                errors.Add($"{game.Name} : {ex.Message}");
            }
        }

        ReloadCards();
        _ = FetchMissingArtworkAsync();
        if (errors.Count > 0)
        {
            _dialogs.ShowError($"{FrenchText.Count(errors.Count, "jeu n'a", "jeux n'ont")} pas été {FrenchText.Agree(errors.Count, "ajouté", "ajoutés")}",
                string.Join("\n", errors.Select(e => $"• {e}")));
        }
    }

    // ---- Page du jeu ----

    /// <summary>Affiche « Mes jeux » sur la page de ce jeu (utilisé par le dock).</summary>
    public void ShowGame(Guid id)
    {
        _navigation.Navigate(this);
        if (OpenGame?.Id == id) return;
        // Une autre fiche a des modifications en attente : même garde que le retour à la grille.
        if (OpenGame is { Editor.IsDirty: true } && !ConfirmDiscard()) return;
        OpenPage(id);
    }

    /// <summary>Retour à la grille (Échap, Alt+←, clic sur « Mes jeux » déjà sélectionné) : même garde que le bouton de la fiche.</summary>
    public void ReturnToGrid()
    {
        if (OpenGame is not null) CloseGamePage();
    }

    /// <summary>Ctrl+F : la vue place le curseur dans la recherche (fiche fermée d'abord, avec sa garde).</summary>
    public event EventHandler? SearchFocusRequested;

    public void RequestSearchFocus()
    {
        _navigation.Navigate(this);
        ReturnToGrid();
        if (OpenGame is null) SearchFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Modifications de la fiche ouverte non enregistrées : vrai si l'utilisateur accepte de les perdre (ou s'il n'y en a pas).</summary>
    public bool ConfirmDiscard() =>
        OpenGame is not { Editor.IsDirty: true } ||
        _dialogs.Confirm("Abandonner les modifications ?",
            $"Vos modifications de {OpenGame.Name} ne sont pas enregistrées : elles seront perdues.", "Abandonner les modifications", isDestructive: true);

    private void OpenPage(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;

        var editor = new ProfileEditorViewModel(profile, Schemes, _programs, _dialogs, SaveProfileAsync, p => RemoveGame(p.Id), () => OpenPage(id), DescribeLaunch);
        var exeName = Path.GetFileName(profile.ExePath);
        var captures = _captures.GetAll()
            .Where(c => c.ProcessName.Equals(exeName, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(c => new CaptureItemViewModel(c, _time, false))
            .ToList();

        OpenGame = new GamePageViewModel(profile, editor, captures,
            back: CloseGamePage,
            changeCover: () => ChangeCoverAsync(id),
            changeBackground: () => ChangeBackgroundAsync(id),
            storeProduct: Core.Library.StorePages.FromProfile(profile),
            openProductPage: OpenProductPageAsync,
            measure: () =>
            {
                _measures.SelectTarget(exeName);
                _navigation.Navigate(_measures);
            },
            play: () => PlayAsync(id),
            togglePin: () => _store.SetPinned(id, _store.Find(id)?.DockOrder is null),
            openStorePage: OpenStorePage,
            setPlayedPreset: preset =>
            {
                _store.SetGraphicsPreset(id, preset);
                _ = LoadRatingsAsync(); // le conseil part de ce réglage
            },
            graphics: new GameGraphicsViewModel(profile, _graphics, _dialogs, _log),
            frameCap: new FrameCapViewModel(profile, _frameCap, _dialogs, _log),
            isPinned: profile.DockOrder.HasValue,
            isPlaying: _sessions.Current?.Profile.Id == id,
            initialTab: _lastGameTab,
            rememberTab: tab => _lastGameTab = tab,
            endSession: _session.EndNowCommand,
            isDockEnabled: () => IsDockEnabled);
        RefreshPagePlaytime(OpenGame);
        _ = LoadPageImagesAsync(OpenGame, profile);
        _ = LoadSteamAppIdAsync(OpenGame, profile);
        _ = OpenGame.Graphics.LoadAsync();
        _ = OpenGame.FrameCap.LoadAsync();
        _ = LoadDiskAsync(OpenGame, profile);
        OpenGame.Rating = Games.FirstOrDefault(c => c.Id == id)?.Rating; // déjà calculée pour la jaquette
        OpenGame.IsRatingLoaded = _ratingsLoaded;
    }

    /// <summary>Disque du jeu (WMI + questions au disque, hors du thread UI). Les jeux Steam sont dans « steamapps\common ».</summary>
    private async Task LoadDiskAsync(GamePageViewModel page, GameProfile profile)
    {
        try
        {
            var isSteam = profile.ExePath.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);
            page.Disk = await Task.Run(() => Platform.Storage.GameDiskReader.Read(profile.ExePath) is { } facts
                ? Core.Library.GameDisk.Assess(facts, isSteam)
                : null);
            page.IsDiskUnreadable = page.Disk is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Management.ManagementException)
        {
            _log.Warn($"Disque de « {profile.Name} » illisible : {ex.Message}");
            page.IsDiskUnreadable = true;
        }
    }

    /// <summary>Appid Steam de la fiche (lecture des manifestes Steam, hors du thread UI) : affiche « Voir sur Steam ».</summary>
    private async Task LoadSteamAppIdAsync(GamePageViewModel page, GameProfile profile)
    {
        try
        {
            page.SteamAppId = await Task.Run(() => _launcher.SteamAppId(profile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Appid Steam de « {profile.Name} » introuvable", ex);
        }
    }

    private void OpenStorePage(string appId)
    {
        try
        {
            _launcher.OpenStorePage(appId);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            _log.Error($"Ouverture de la page Steam {appId} impossible", ex);
            _dialogs.ShowError("Impossible de voir le jeu sur Steam", "Ni Steam ni le navigateur n'ont pu ouvrir sa page du magasin Steam.", ex.Message);
        }
    }

    private void CloseGamePage()
    {
        if (!ConfirmDiscard()) return;
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
            _dialogs.ShowError("Réglages non enregistrés", ex.Message);
            return Task.CompletedTask;
        }
        ReloadCards();
        OpenPage(profile.Id); // Recharge la page depuis la version enregistrée (plus de modifications en attente).
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retire un jeu de « Mes jeux » (corbeille de la jaquette, menu contextuel, page du jeu), après confirmation. Seul le profil
    /// est supprimé : rien n'est désinstallé ni modifié dans Windows. Jamais le jeu en cours (sa session est restaurée à la fin).
    /// </summary>
    private void RemoveGame(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;
        if (_sessions.Current?.Profile.Id == id)
        {
            _dialogs.ShowInfo($"{profile.Name} est en cours", "Vous pourrez le retirer de Mes jeux une fois la partie terminée.");
            return;
        }
        if (!_dialogs.Confirm($"Retirer {profile.Name} de Mes jeux ?",
                "Seuls ses réglages OptiGame sont supprimés : le jeu reste installé et aucun réglage de Windows n'est modifié. " +
                "Vous pourrez le rajouter plus tard." + KeptChangesText([id]),
                "Retirer le jeu", isDestructive: true))
        {
            return;
        }
        _store.Remove(id);
        _artwork.DeleteCustomHero(profile.CustomHeroFile); // fond choisi : plus utilisé
        _log.Info($"Jeu retiré de « Mes jeux » : {profile.Name} ({profile.ExePath}).");
        if (OpenGame?.Id == id) OpenGame = null;
        ReloadCards();
    }

    /// <summary>
    /// Réglages faits pour ces jeux depuis leur page (carte graphique, plafond de FPS…) : les retirer de « Mes jeux » ne les annule
    /// pas, la confirmation les cite avec le moyen de les annuler. Vide s'il n'y en a pas.
    /// </summary>
    private string KeptChangesText(IReadOnlyCollection<Guid> ids)
    {
        var active = _fixes.ActiveChanges;
        var kept = ids.SelectMany(id => GameChanges.Of(active, id)).ToList();
        if (kept.Count == 0) return "";
        return $"\n\nCes réglages faits pour {(ids.Count == 1 ? "ce jeu" : "ces jeux")} restent en place :\n" +
               string.Join("\n", kept.Select(c => "• " + c.Title)) +
               "\nPour les restaurer, faites-le depuis la fiche du jeu avant de le retirer, ou plus tard depuis la page Diagnostic.";
    }

    private async Task ChangeCoverAsync(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;
        if (!_igdb.IsConfigured)
        {
            _dialogs.ShowInfo("Identifiants IGDB requis", "Pour choisir une jaquette, renseignez d'abord vos identifiants IGDB (Twitch) dans Paramètres.");
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

    /// <summary>
    /// « Changer le fond… » : bannière Steam du PC, illustrations et captures IGDB, ou image du PC. L'image choisie est copiée dans
    /// covers\heroes (le fond ne dépend plus de Steam ni d'IGDB) ; l'ancien fond choisi est supprimé.
    /// </summary>
    private async Task ChangeBackgroundAsync(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;
        var appId = OpenGame?.Id == id ? OpenGame.SteamAppId ?? profile.SteamAppId : profile.SteamAppId;
        var steamHero = uint.TryParse(appId, out var steamId) ? await Task.Run(() => SteamOwnedLibrary.HeroPath(steamId)) : null;
        Func<Task<IReadOnlyList<IgdbBackground>>>? loadIgdb = _igdb.IsConfigured
            ? async () =>
            {
                var gameId = profile.IgdbGameId ?? Igdb.BestMatch(profile.Name, await _igdb.SearchAsync(profile.Name))?.Id;
                return gameId is { } igdbId ? await _igdb.BackgroundsAsync(igdbId) : [];
            }
            : null;

        var choice = _dialogs.PickBackground(profile.Name, steamHero, loadIgdb,
            imageId => _artwork.GetAsync(imageId, Igdb.BackgroundThumbSize), profile.CustomHeroFile is not null);
        if (choice is null) return;

        string? file = null;
        if (choice.Source != Dialogs.BackgroundSource.Original)
        {
            var source = choice.Source == Dialogs.BackgroundSource.Igdb ? await _artwork.GetAsync(choice.ImageId, Igdb.HeroSize) : choice.Path;
            if (source is null)
            {
                _dialogs.ShowError("Fond indisponible", "L'image n'a pas pu être téléchargée depuis IGDB. Vérifiez la connexion à Internet, puis réessayez.");
                return;
            }
            try
            {
                file = await Task.Run(() => _artwork.StoreCustomHero(id, source));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error($"Fond de « {profile.Name} » non enregistré", ex);
                _dialogs.ShowError("Fond non enregistré", "L'image n'a pas pu être copiée dans le dossier des données d'OptiGame.", ex.Message);
                return;
            }
        }

        // ArtworkChanged recharge les images de la fiche ouverte.
        var previous = _store.SetCustomHero(id, file);
        if (previous != file) _artwork.DeleteCustomHero(previous);
        _log.Info(file is null ? $"Fond d'origine rétabli pour « {profile.Name} »." : $"Fond choisi pour « {profile.Name} » ({choice.Source}).");
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
                if (_gate.InGame)
                {
                    _gate.RunOrDefer("igdb", () => _ = FetchMissingArtworkAsync()); // la suite à la fin de la partie
                    break;
                }
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

    // ---- Jeux désinstallés ----

    private int _installVersion;
    private Task _installCheck = Task.CompletedTask;
    private Task _libraryLoad = Task.CompletedTask;

    /// <summary>Jeux de « Mes jeux » dont l'exe a disparu de son disque (le disque, lui, est là).</summary>
    public IReadOnlyList<GameCardViewModel> UninstalledProfiles => Games.Where(c => c.IsUninstalled).ToList();

    public bool HasUninstalledProfiles => Games.Any(c => c.IsUninstalled);

    public string UninstalledProfilesText => UninstalledProfiles switch
    {
        [var one] => $"« {one.Name} » n'est plus installé : son fichier .exe a disparu.",
        var many => $"{many.Count} jeux ne sont plus installés : {string.Join(", ", many.Select(c => c.Name))}.",
    };

    /// <summary>Vérifie, hors du thread UI, que l'exe de chaque profil est toujours là (quelques File.Exists).</summary>
    private async Task CheckInstallationsAsync()
    {
        var version = ++_installVersion;
        var profiles = _store.GetAll().Select(p => (p.Id, p.ExePath)).ToList();
        var states = await Task.Run(() => profiles.ToDictionary(p => p.Id, p => GameInstallation.Of(p.ExePath, File.Exists, Directory.Exists)));
        if (version != _installVersion) return;
        foreach (var card in Games)
        {
            if (states.TryGetValue(card.Id, out var state)) card.InstallState = state;
        }
        OnPropertyChanged(nameof(UninstalledProfiles));
        OnPropertyChanged(nameof(HasUninstalledProfiles));
        OnPropertyChanged(nameof(UninstalledProfilesText));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshLibraryCommand))]
    private bool _isRefreshing;

    private bool CanRefresh() => !IsRefreshing;

    /// <summary>
    /// « Actualiser » : relit tout ce qui vient du disque (exe des profils, bibliothèques Steam / Epic / GOG, nouveaux jeux Steam).
    /// Lecture seule : un jeu désinstallé est seulement signalé.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshLibraryAsync()
    {
        IsRefreshing = true;
        try
        {
            ReloadCards();
            await Task.WhenAll(_installCheck, _libraryLoad, NewGames.CheckAsync());
            var missing = UninstalledProfiles.Count;
            ShowLaunchStatus(missing switch
            {
                0 => "Bibliothèque actualisée : tous vos jeux sont installés.",
                1 => "Bibliothèque actualisée : 1 jeu n'est plus installé.",
                _ => $"Bibliothèque actualisée : {missing} jeux ne sont plus installés.",
            }, missing == 0 ? Controls.Severity.Success : Controls.Severity.Warning);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Retire de « Mes jeux » les jeux désinstallés, après confirmation (jamais ceux dont le disque est absent).</summary>
    [RelayCommand]
    private void RemoveUninstalledProfiles()
    {
        var playing = _sessions.Current?.Profile.Id;
        var games = UninstalledProfiles.Where(c => c.Id != playing).ToList();
        if (games.Count == 0) return;
        var names = string.Join("\n", games.Select(c => "• " + c.Name));
        if (!_dialogs.Confirm(games.Count == 1 ? $"Retirer {games[0].Name} de Mes jeux ?" : $"Retirer ces {games.Count} jeux de Mes jeux ?",
                $"{names}\n\nSeuls leurs réglages OptiGame sont supprimés ; aucun réglage de Windows n'est modifié. Ceux de vos " +
                "bibliothèques Steam, Epic Games ou GOG resteront parmi les jeux non installés." +
                KeptChangesText(games.Select(c => c.Id).ToList()),
                games.Count == 1 ? "Retirer le jeu" : $"Retirer les {games.Count} jeux", isDestructive: true))
        {
            return;
        }
        foreach (var card in games)
        {
            var heroFile = _store.Find(card.Id)?.CustomHeroFile;
            _store.Remove(card.Id);
            _artwork.DeleteCustomHero(heroFile);
            _log.Info($"Jeu désinstallé retiré de « Mes jeux » : {card.Name}.");
        }
        ReloadCards();
    }

    private void ReloadCards()
    {
        var installStates = Games.ToDictionary(c => c.Id, c => c.InstallState); // état gardé jusqu'à la nouvelle vérification
        Games.Clear();
        var playing = _sessions.Current?.Profile.Id;
        foreach (var profile in _store.GetAll().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var card = new GameCardViewModel(profile, playing == profile.Id)
            {
                Owner = this,
                CoverPath = _artwork.TryGetCached(profile.CoverImageId, Igdb.CoverSize),
                Playtime = PlaytimeOf(profile.Id, _time.GetLocalNow()),
                InstallState = installStates.GetValueOrDefault(profile.Id, InstallState.Installed),
            };
            Games.Add(card);
            if (card.CoverPath is null && profile.CoverImageId is not null)
            {
                _ = RefreshCoverAsync(profile.Id);
            }
        }
        OnPropertyChanged(nameof(HasGames));
        NotifyCounts();
        _ = LoadSteamPlaytimeAsync(); // profils ajoutés, retirés ou modifiés : leur jeu Steam a pu changer
        _ = LoadRatingsAsync();
        _libraryLoad = Logged(LoadLibrariesAsync(), "Lecture des bibliothèques des magasins"); // genres et types des cartes recréées, jeux non installés
        _installCheck = Logged(CheckInstallationsAsync(), "Vérification des jeux installés"); // jeux désinstallés depuis
    }

    private async Task RefreshCoverAsync(Guid id)
    {
        if (_store.Find(id) is not { } profile) return;
        var path = await _artwork.GetAsync(profile.CoverImageId, Igdb.CoverSize);
        if (Games.FirstOrDefault(g => g.Id == id) is { } card) card.CoverPath = path;
        if (OpenGame?.Id == id) await LoadPageImagesAsync(OpenGame, profile);
    }

    /// <summary>
    /// Fond de la fiche : celui choisi par l'utilisateur, sinon la bannière que Steam garde sur le PC (faite pour servir de fond),
    /// sinon la première illustration IGDB (parfois un simple logo : Portal 2).
    /// </summary>
    private async Task LoadPageImagesAsync(GamePageViewModel page, GameProfile profile)
    {
        page.CoverPath = await _artwork.GetAsync(profile.CoverImageId, Igdb.CoverSize);
        page.HeroPath = _artwork.CustomHeroPath(profile.CustomHeroFile)
            ?? (uint.TryParse(profile.SteamAppId, out var appId) ? await Task.Run(() => SteamOwnedLibrary.HeroPath(appId)) : null)
            ?? await _artwork.GetAsync(profile.HeroImageId, Igdb.HeroSize);
    }

    private void RefreshPlaying()
    {
        var playing = _sessions.Current?.Profile.Id;
        foreach (var card in Games) card.IsPlaying = card.Id == playing;
        if (OpenGame is not null) OpenGame.IsPlaying = OpenGame.Id == playing;
    }

    internal static string CleanName(string name) => name.Replace("®", "").Replace("™", "").Trim();

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}

/// <summary>Jaquette de la grille.</summary>
public sealed partial class GameCardViewModel(GameProfile profile, bool isPlaying) : ObservableObject
{

    /// <summary>
    /// Page Mes jeux : les boutons de survol de la jaquette s'y lient (« Owner.PlayCardCommand »). Une liaison RelativeSource vers
    /// le UserControl ÉCHOUE pour ces boutons (créés dans CoverTile.Actions, hors de l'arbre visuel : PathError, jamais retentée ;
    /// constaté le 2026-10-06, les boutons n'avaient aucune commande et le clic retombait sur la jaquette).
    /// </summary>
    public LibraryViewModel? Owner { get; set; }
    public Guid Id { get; } = profile.Id;

    public string Name { get; } = profile.Name;

    public string ExeName { get; } = Path.GetFileName(profile.ExePath);

    public bool Enabled { get; } = profile.Enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _isPinned = profile.DockOrder.HasValue;

    /// <summary>Initiales affichées tant qu'il n'y a pas de jaquette.</summary>
    public string Initials { get; } = string.Concat(profile.Name
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(w => char.IsLetterOrDigit(w[0]))
        .Take(2)
        .Select(w => char.ToUpperInvariant(w[0])));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(CoverImage))]
    private string? _coverPath;

    public bool HasCover => CoverPath is not null;

    /// <summary>Jaquette décodée ; lue par une liaison IsAsync (hors du thread UI), gardée par <see cref="Converters.ImageLoader"/>.</summary>
    public System.Windows.Media.ImageSource? CoverImage =>
        Converters.ImageLoader.Load(CoverPath, Converters.ImageLoader.PixelsFor(Converters.ImageLoader.GridCoverWidth));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _isPlaying = isPlaying;

    /// <summary>Exe du profil toujours sur le disque ? Vérifié à chaque rechargement de la grille et par « Actualiser ».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUninstalled), nameof(IsDriveUnavailable), nameof(IsDimmed), nameof(AccessibleName))]
    private InstallState _installState = InstallState.Installed;

    public bool IsUninstalled => InstallState == InstallState.Uninstalled;

    /// <summary>Jaquette atténuée : jeu désinstallé ou disque absent.</summary>
    public bool IsDimmed => InstallState != InstallState.Installed;

    /// <summary>
    /// Nom lu par les lecteurs d'écran : le jeu et tout ce que les pastilles disent (rien n'est porté par la seule image ou
    /// une info-bulle) ; « Entrée » ouvre la fiche, la touche Menu donne les actions.
    /// </summary>
    public string AccessibleName => string.Join(", ", new[]
    {
        Name,
        IsPlaying ? "partie en cours" : null,
        IsUninstalled ? "désinstallé" : null,
        IsDriveUnavailable ? "disque absent" : null,
        Enabled ? null : "optimisation désactivée",
        Rating is null ? null : $"note {Rating.Score} sur 100 ({(Rating.Source == Core.Rating.RatingSource.Measured ? "mesurée" : "estimée")})",
        IsPinned ? "épinglé au dock" : null,
        StoreLabel,
        PlaytimeText,
    }.OfType<string>());

    public bool IsDriveUnavailable => InstallState == InstallState.DriveUnavailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaytimeText), nameof(LastPlayedTicks), nameof(TotalTicks), nameof(AccessibleName))]
    private PlaytimeSummary _playtime = PlaytimeSummary.None;

    /// <summary>Sous le nom de la jaquette : « 12 h 05 » (temps Steam compris) ou « Jamais joué ».</summary>
    public string PlaytimeText => Playtime.EverPlayed ? Core.Playtime.PlaytimeText.Duration(Playtime.Total) : "Jamais joué";

    /// <summary>Clé de tri « Temps de jeu ».</summary>
    public long TotalTicks => Playtime.Total.Ticks;

    /// <summary>Note du jeu (mesurée ou estimée) ; null = pas de pastille.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRating), nameof(RatingBadge), nameof(RatingLevel), nameof(RatingTooltip), nameof(AccessibleName))]
    private Core.Rating.GameRating? _rating;

    public bool HasRating => Rating is not null;

    /// <summary>« 77 » mesuré, « ≈ 77 » estimé.</summary>
    public string RatingBadge => Rating is null ? "" : Rating.Source == Core.Rating.RatingSource.Measured ? $"{Rating.Score}" : $"≈ {Rating.Score}";

    /// <summary>Couleur de la pastille : Good (75 et plus), Fair (50 et plus), Poor.</summary>
    public string RatingLevel => Rating?.Score switch { >= 75 => "Good", >= 50 => "Fair", _ => "Poor" };

    public string RatingTooltip => Rating is null ? ""
        : $"{Rating.Headline} — {Rating.Score}/100" +
          (Rating.Preset is { } preset ? $", réglage conseillé : {Core.Rating.GameRatings.Label(preset)}" : "") +
          (Rating.Source == Core.Rating.RatingSource.Measured ? " (mesuré)" : " (estimation)");

    /// <summary>Clé de tri « Récemment joués » (0 = jamais joué, en fin de liste).</summary>
    public long LastPlayedTicks => Playtime.LastPlayed?.UtcTicks ?? 0;

    /// <summary>Genres et types de son magasin (Steam) ; ceux d'IGDB s'y ajoutent (LibraryViewModel.ApplyTags).</summary>
    public Core.Library.GameTags StoreTags { get; set; } = Core.Library.GameTags.Empty;

    /// <summary>Genres et types affichés et filtrés : magasin + IGDB (vides tant que rien n'est connu).</summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>Magasin du jeu (filtre et pastille de la jaquette) ; null = inconnu (jeu ajouté à la main).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoreLabel), nameof(HasStore), nameof(AccessibleName))]
    private GameSource? _store;

    public bool HasStore => Store is not null and not GameSource.Folder;

    public string? StoreLabel => HasStore ? Core.Library.StoreCatalogs.Label(Store!.Value) : null;

    public IReadOnlySet<Core.Library.GameKind> Kinds { get; set; } = new HashSet<Core.Library.GameKind>();
}

/// <summary>
/// Jeu possédé mais non installé (section grisée de « Mes jeux »), quel que soit le magasin : Steam (caches du client, jaquette
/// locale) ou Epic et GOG (jaquette téléchargée une fois depuis le CDN du magasin).
/// </summary>
public sealed partial class OwnedGameCardViewModel : ObservableObject
{

    /// <summary>
    /// Page Mes jeux : les boutons de survol de la jaquette s'y lient (« Owner.PlayCardCommand »). Une liaison RelativeSource vers
    /// le UserControl ÉCHOUE pour ces boutons (créés dans CoverTile.Actions, hors de l'arbre visuel : PathError, jamais retentée ;
    /// constaté le 2026-10-06, les boutons n'avaient aucune commande et le clic retombait sur la jaquette).
    /// </summary>
    public LibraryViewModel? Owner { get; set; }
    private OwnedGameCardViewModel(GameSource store, string key, string name, IReadOnlyList<string> genres,
        IReadOnlySet<Core.Library.GameKind> kinds, string? coverPath, string? coverUrl)
    {
        Store = store;
        Key = key;
        Name = name;
        _storeTags = new Core.Library.GameTags(genres, kinds);
        Genres = genres;
        Kinds = kinds;
        _coverPath = coverPath;
        CoverUrl = coverUrl;
        Initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0]))
            .Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    /// <summary>Genres et types du magasin (Steam, GOG Galaxy) ; ceux d'IGDB s'y ajoutent.</summary>
    private readonly Core.Library.GameTags _storeTags;

    /// <summary>Genres IGDB arrivés (ou null) : genres affichés et filtrés = magasin + IGDB.</summary>
    public void ApplyTags(Core.Library.GameTags? igdb)
    {
        var tags = _storeTags.With(igdb);
        if (tags.Genres.SequenceEqual(Genres) && tags.Kinds.SetEquals(Kinds)) return;
        (Genres, Kinds) = (tags.Genres, tags.Kinds);
        OnPropertyChanged(nameof(GenresText));
        OnPropertyChanged(nameof(CaptionText));
        OnPropertyChanged(nameof(AccessibleName));
    }

    public static OwnedGameCardViewModel FromSteam(Core.Library.OwnedSteamGame game, string? coverPath) =>
        new(GameSource.Steam, game.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture), game.Name,
            game.Genres.Select(Core.Library.SteamTaxonomy.Genre).OfType<string>().ToList(), Core.Library.SteamTaxonomy.Kinds(game.Categories),
            coverPath, null);

    public static OwnedGameCardViewModel FromStore(Core.Library.StoreOwnedGame game, string? cachedCover) =>
        new(game.Store, game.Key, game.Name, game.Genres, new HashSet<Core.Library.GameKind>(), cachedCover, game.CoverUrl);

    public GameSource Store { get; }

    /// <summary>Appid Steam, « namespace:item:app » Epic, ou releaseKey de GOG Galaxy.</summary>
    public string Key { get; }

    public string Name { get; }

    public IReadOnlyList<string> Genres { get; private set; }

    public IReadOnlySet<Core.Library.GameKind> Kinds { get; private set; }

    public string? CoverUrl { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(CoverImage))]
    private string? _coverPath;

    public bool HasCover => CoverPath is not null;

    /// <summary>Jaquette grisée ; lue par une liaison IsAsync (hors du thread UI), gardée par <see cref="Converters.ImageLoader"/>.</summary>
    public System.Windows.Media.ImageSource? CoverImage =>
        Converters.ImageLoader.Load(CoverPath, Converters.ImageLoader.PixelsFor(Converters.ImageLoader.GridCoverWidth), gray: true);

    public string Initials { get; }

    public string GenresText => string.Join(" · ", Genres.Take(2));

    public string StoreLabel => Core.Library.StoreCatalogs.Label(Store);

    /// <summary>Steam et Epic ouvrent leur installation ; GOG ouvre la page du jeu dans GOG Galaxy, d'où l'installer.</summary>
    public string InstallLabel => Store == GameSource.Gog ? "Ouvrir dans GOG Galaxy" : "Installer";

    /// <summary>« Non installé · Steam » sous la jaquette, avec les genres s'il y en a.</summary>
    public string CaptionText => GenresText.Length > 0 ? $"{StoreLabel} · {GenresText}" : StoreLabel;

    public string AccessibleName => $"{Name}, non installé, {StoreLabel}" + (GenresText.Length > 0 ? $", {GenresText}" : "") +
                                    $". Entrée : {InstallLabel.ToLowerInvariant()}";

    /// <summary>Epic Games et GOG : produit du magasin (« Voir sur … »), lu dans la clé du jeu possédé.</summary>
    public Core.Library.StoreProduct? Product => Core.Library.StorePages.FromOwned(Store, Key, Name);

    public bool HasStorePage => Store == GameSource.Steam || Product is not null;

    public string StorePageLabel => Store == GameSource.Steam ? "Voir sur Steam" : Core.Library.StorePages.Label(Store);
}

/// <summary>Choix d'un filtre de « Mes jeux » (valeur null = tous).</summary>
public sealed record GenreOption(string? Name, string Label);

public sealed record StoreOption(GameSource? Store, string Label);
public sealed record KindOption(Core.Library.GameKind? Kind, string Label);
