using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.Core.Playtime;
using OptiGame.Core.Profiles;

namespace OptiGame.App.ViewModels;

/// <summary>Onglets de la fiche du jeu.</summary>
public enum GameTab
{
    Overview,
    Optimization,
    Properties,
}

/// <summary>
/// Fiche d'un jeu : bannière (Jouer, menu « … »), puis trois onglets — Vue d'ensemble (note, temps de jeu, mesures, disque),
/// Optimisation (réglages de partie, enregistrés par « Enregistrer » ; réglages permanents de Windows et du pilote, appliqués
/// tout de suite après confirmation), Propriétés (nom, fichier .exe, lancement, retrait).
/// </summary>
public sealed partial class GamePageViewModel(
    GameProfile profile,
    ProfileEditorViewModel editor,
    IReadOnlyList<CaptureItemViewModel> captures,
    Action back,
    Func<Task> changeCover,
    Action measure,
    Func<Task> play,
    Action togglePin,
    Action<string> openStorePage,
    Action<Core.Rating.GraphicsPreset?> setPlayedPreset,
    GameGraphicsViewModel graphics,
    FrameCapViewModel frameCap,
    bool isPinned,
    bool isPlaying,
    GameTab initialTab,
    Action<GameTab> rememberTab,
    System.Windows.Input.ICommand endSession,
    Func<bool> isDockEnabled) : ObservableObject
{
    // ---- Onglets (le dernier choisi est gardé d'une fiche à l'autre) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTab), nameof(IsOptimizationTab), nameof(IsPropertiesTab))]
    private GameTab _selectedTab = initialTab;

    partial void OnSelectedTabChanged(GameTab value) => rememberTab(value);

    public bool IsOverviewTab
    {
        get => SelectedTab == GameTab.Overview;
        set { if (value) SelectedTab = GameTab.Overview; }
    }

    public bool IsOptimizationTab
    {
        get => SelectedTab == GameTab.Optimization;
        set { if (value) SelectedTab = GameTab.Optimization; }
    }

    public bool IsPropertiesTab
    {
        get => SelectedTab == GameTab.Properties;
        set { if (value) SelectedTab = GameTab.Properties; }
    }

    /// <summary>Le dock est activé : « Épingler au dock » proposé dans le menu « … ».</summary>
    public bool IsDockEnabled => isDockEnabled();

    /// <summary>Pendant la partie : « Arrêter l'optimisation… » (confirmation, puis restauration) à la place de « Jouer ».</summary>
    public System.Windows.Input.ICommand EndSessionCommand { get; } = endSession;

    /// <summary>Les notes de la bibliothèque ont été calculées au moins une fois : « pas de note » n'est dit qu'après.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRatingPending), nameof(HasNoRating))]
    private bool _isRatingLoaded;

    public bool IsRatingPending => !IsRatingLoaded && Rating is null;

    public bool HasNoRating => IsRatingLoaded && Rating is null;

    /// <summary>Note du jeu par rapport au PC (mesurée ou estimée) ; null = pas encore de note.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRating), nameof(ScoreText), nameof(PresetText), nameof(RatingHeadline), nameof(RatingAdvice),
        nameof(RatingSource), nameof(RatingDetails), nameof(RatingLevel), nameof(HasRequirementsLink),
        nameof(IsRatingPending), nameof(HasNoRating), nameof(RatingKind))]
    private Core.Rating.GameRating? _rating;

    public bool HasRating => Rating is not null;

    public string ScoreText => Rating is null ? "" : $"{Rating.Score}";

    /// <summary>« mesurée » ou « estimée » (le caractère estimé d'une note est toujours dit).</summary>
    public string RatingKind => Rating?.Source == Core.Rating.RatingSource.Measured ? "Note mesurée" : "Note estimée";

    public string PresetText => Rating?.Preset is not { } preset ? ""
        : $"Réglage conseillé : {Core.Rating.GameRatings.Label(preset)}" +
          (Rating.Source == Core.Rating.RatingSource.Measured && preset == PlayedPreset.Value ? ", celui que vous utilisez" : "");

    /// <summary>Choix du réglage utilisé dans le jeu (OptiGame ne peut pas le lire dans les fichiers du jeu).</summary>
    public IReadOnlyList<PlayedPresetOption> PlayedPresetOptions => AllPlayedPresets;

    private static readonly IReadOnlyList<PlayedPresetOption> AllPlayedPresets =
    [
        new(null, "Non indiqué"),
        new(Core.Rating.GraphicsPreset.Low, "Bas"),
        new(Core.Rating.GraphicsPreset.Medium, "Moyen"),
        new(Core.Rating.GraphicsPreset.High, "Élevé"),
        new(Core.Rating.GraphicsPreset.Ultra, "Ultra"),
    ];

    /// <summary>Réglage utilisé dans le jeu ; enregistré dans le profil, puis la note est recalculée.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetText))]
    private PlayedPresetOption _playedPreset = AllPlayedPresets.First(o => o.Value == profile.GraphicsPreset);

    partial void OnPlayedPresetChanged(PlayedPresetOption value) => setPlayedPreset(value.Value);

    public string RatingHeadline => Rating?.Headline ?? "";

    public string RatingAdvice => Rating?.Advice ?? "";

    public string RatingSource => Rating?.Source == Core.Rating.RatingSource.Measured
        ? "Mesuré pendant vos parties (PresentMon)."
        : $"Estimation d'après la configuration requise du jeu ({SourceLabel(Rating?.RequirementsSource)}) et votre PC.";

    private static string SourceLabel(string? source) =>
        source == Core.Rating.PcGamingWiki.SourceName ? "PCGamingWiki, licence CC BY-NC-SA" : source ?? "Steam";

    /// <summary>Lien vers la page citée (PCGamingWiki) ; absent pour Steam, accessible par « Page Steam ».</summary>
    public bool HasRequirementsLink => Rating?.RequirementsUrl is not null;

    [RelayCommand]
    private void OpenRequirementsSource()
    {
        // explorer.exe transmet l'adresse au navigateur de la session, sans droits administrateur.
        if (Rating?.RequirementsUrl is { } url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", url) { UseShellExecute = true });
        }
    }

    public IReadOnlyList<string> RatingDetails => Rating?.Details ?? [];

    /// <summary>Couleur : Good (75 et plus), Fair (50 et plus), Poor.</summary>
    public string RatingLevel => Rating?.Score switch { >= 75 => "Good", >= 50 => "Fair", _ => "Poor" };

    /// <summary>Carte « Graphismes (Windows) » : Auto HDR et carte graphique pour ce jeu.</summary>
    public GameGraphicsViewModel Graphics { get; } = graphics;

    /// <summary>Carte « Pilote NVIDIA » : plafond de FPS dans le profil du pilote.</summary>
    public FrameCapViewModel FrameCap { get; } = frameCap;

    /// <summary>Appid Steam, retrouvé en arrière-plan à l'ouverture de la page ; null pour un jeu hors Steam.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStorePage))]
    private string? _steamAppId;

    public bool HasStorePage => SteamAppId is not null;

    [RelayCommand]
    private void OpenStorePage()
    {
        if (SteamAppId is { } appId) openStorePage(appId);
    }

    [ObservableProperty]
    private bool _isPinned = isPinned;

    [RelayCommand]
    private void TogglePin() => togglePin();

    public Guid Id { get; } = profile.Id;

    public string Name { get; } = profile.Name;

    public string ExeName { get; } = Path.GetFileName(profile.ExePath);

    public string Initials { get; } = string.Concat(profile.Name
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    public ProfileEditorViewModel Editor { get; } = editor;

    public IReadOnlyList<CaptureItemViewModel> Captures { get; } = captures;

    public bool HasCaptures => Captures.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(CoverImage))]
    private string? _coverPath;

    public bool HasCover => CoverPath is not null;

    /// <summary>Jaquette décodée hors du thread de l'interface (liaison IsAsync), comme la grille.</summary>
    public System.Windows.Media.ImageSource? CoverImage => Converters.ImageLoader.Load(CoverPath, Converters.ImageLoader.PixelsFor(135));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroImage))]
    private string? _heroPath;

    /// <summary>Bannière décodée hors du thread de l'interface (liaison IsAsync).</summary>
    public System.Windows.Media.ImageSource? HeroImage => Converters.ImageLoader.Load(HeroPath, Converters.ImageLoader.PixelsFor(1280));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isPlaying = isPlaying;

    // ---- Temps de jeu ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaytimeSummary))]
    private string _playtimeTotal = "Jamais joué";

    [ObservableProperty] private string _playtimeDetail = "";
    [ObservableProperty] private IReadOnlyList<SessionRow> _recentSessions = [];

    /// <summary>« Dernière partie hier » (tuile du temps de jeu) ; vide si jamais joué.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaytimeSummary))]
    private string _playtimeLast = "";

    public string PlaytimeSummary => PlaytimeLast.Length > 0 ? $"{PlaytimeTotal} · {PlaytimeLast.ToLowerInvariant()}" : PlaytimeTotal;

    // ---- Mesures (tuile et ligne de la liste) ----

    public string MeasuresSummary => Captures.FirstOrDefault() is { } latest
        ? $"{Core.Text.FrenchText.Count(Captures.Count, "mesure", "mesures")} · dernière : {latest.AverageFps} FPS"
        : "Aucune mesure : « Mesurer les FPS », ou jouez plus de 5 minutes (mesure automatique).";

    /// <summary>Disque d'installation (lu en arrière-plan à l'ouverture de la page) ; null tant qu'il n'est pas lu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisk), nameof(DiskLevel), nameof(DiskTile), nameof(DiskSummary), nameof(IsDiskPending))]
    private Core.Library.GameDiskReport? _disk;

    /// <summary>Le disque n'a pas pu être lu (fin de « Lecture… », jamais d'attente infinie).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiskPending), nameof(DiskSummary))]
    private bool _isDiskUnreadable;

    public bool IsDiskPending => Disk is null && !IsDiskUnreadable;

    public string DiskTile => Disk?.Type ?? "Disque";

    public string DiskSummary => Disk?.Detail ?? (IsDiskUnreadable ? "Disque illisible pour l'instant (disque externe débranché, droits…)." : "Lecture du disque…");

    public bool HasDisk => Disk is not null;

    /// <summary>Couleur du titre : Good (bon emplacement), Fair (à surveiller), Poor (disque dur ou presque plein).</summary>
    public string DiskLevel => Disk?.Level switch
    {
        Core.Library.GameDiskLevel.Warning => "Poor",
        Core.Library.GameDiskLevel.Info => "Fair",
        _ => "Good",
    };

    public void SetPlaytime(PlaytimeSummary summary, IReadOnlyList<PlaySession> recent, DateTimeOffset now)
    {
        PlaytimeTotal = summary.EverPlayed ? PlaytimeText.Duration(summary.Total) : "Jamais joué";
        var last = summary.LastPlayed is { } when ? $"dernière partie {PlaytimeText.LastPlayed(when, now)}" : null;
        var tracked = summary.OptiGameSessions switch
        {
            0 => null,
            1 => "1 partie suivie par OptiGame",
            var n => $"{n} parties suivies par OptiGame",
        };
        PlaytimeDetail = summary.Source switch
        {
            // Steam compte tout, y compris avant OptiGame et en dehors : c'est le total affiché.
            PlaytimeSource.Steam => string.Join(" · ", new[] { "Selon Steam", last, tracked }.OfType<string>()),
            PlaytimeSource.OptiGame => string.Join(" — ", new[] { tracked, last }.OfType<string>()),
            _ => "Le temps est compté automatiquement pendant les parties détectées par OptiGame (et repris de Steam pour les jeux Steam).",
        };
        PlaytimeLast = summary.LastPlayed is { } lastPlayed ? $"Dernière partie {PlaytimeText.LastPlayed(lastPlayed, now)}" : "";
        RecentSessions = recent.Select(s => new SessionRow(
            Core.Text.FrenchText.When(s.StartedAt.ToLocalTime().DateTime, now.ToLocalTime().DateTime),
            s.Incomplete ? "durée inconnue" : s.Duration is { } d ? PlaytimeText.Duration(d) : "en cours")).ToList();
    }

    [RelayCommand]
    private void Back() => back();

    [RelayCommand]
    private Task ChangeCoverAsync() => changeCover();

    [RelayCommand]
    private void Measure() => measure();

    private bool CanPlay() => !IsPlaying;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task PlayAsync() => play();
}

/// <summary>Ligne « Dernières parties » de la fiche du jeu.</summary>
public sealed record SessionRow(string Date, string Duration);

public sealed record PlayedPresetOption(Core.Rating.GraphicsPreset? Value, string Label);
