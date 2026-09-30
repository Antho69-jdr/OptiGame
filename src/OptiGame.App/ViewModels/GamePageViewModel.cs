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
    bool isPinned,
    bool isPlaying) : ObservableObject
{
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
