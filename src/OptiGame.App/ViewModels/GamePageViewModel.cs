using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.Core.Playtime;
using OptiGame.Core.Profiles;

namespace OptiGame.App.ViewModels;

/// <summary>Page d'un jeu : bannière, jaquette, réglages du profil (éditeur), captures de ce jeu.</summary>
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
    bool isPinned,
    bool isPlaying) : ObservableObject
{
    /// <summary>Note du jeu par rapport au PC (mesurée ou estimée) ; null = pas encore de note.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRating), nameof(ScoreText), nameof(PresetText), nameof(RatingHeadline), nameof(RatingAdvice),
        nameof(RatingSource), nameof(RatingDetails), nameof(RatingLevel), nameof(HasRequirementsLink))]
    private Core.Rating.GameRating? _rating;

    public bool HasRating => Rating is not null;

    public string ScoreText => Rating is null ? "" : $"{Rating.Score}";

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
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverPath;

    public bool HasCover => CoverPath is not null;

    [ObservableProperty]
    private string? _heroPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isPlaying = isPlaying;

    // ---- Temps de jeu ----

    [ObservableProperty] private string _playtimeTotal = "Jamais joué";
    [ObservableProperty] private string _playtimeDetail = "";
    [ObservableProperty] private IReadOnlyList<SessionRow> _recentSessions = [];

    /// <summary>Disque d'installation (lu en arrière-plan à l'ouverture de la page) ; null tant qu'il n'est pas lu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisk), nameof(DiskLevel))]
    private Core.Library.GameDiskReport? _disk;

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
            _ => "Le temps est compté automatiquement pendant les sessions détectées par OptiGame (et repris de Steam pour les jeux Steam).",
        };
        RecentSessions = recent.Select(s => new SessionRow(
            s.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
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

/// <summary>Ligne « Dernières parties » de la page du jeu.</summary>
public sealed record SessionRow(string Date, string Duration);

public sealed record PlayedPresetOption(Core.Rating.GraphicsPreset? Value, string Label);
