using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    bool isPlaying) : ObservableObject
{
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
    private bool _isPlaying = isPlaying;

    [RelayCommand]
    private void Back() => back();

    [RelayCommand]
    private Task ChangeCoverAsync() => changeCover();

    [RelayCommand]
    private void Measure() => measure();
}
