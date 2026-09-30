using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.Core.Artwork;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Platform.Artwork;

namespace OptiGame.App.ViewModels;

/// <summary>Contenu du dock flottant : jeux épinglés (ordre du profil), jeu en cours, actions.</summary>
public sealed partial class DockViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly ArtworkCache _artwork;
    private readonly GameSessionManager _sessions;
    private readonly LibraryViewModel _library;

    public DockViewModel(ProfileStore store, ArtworkCache artwork, GameSessionManager sessions, LibraryViewModel library)
    {
        _store = store;
        _artwork = artwork;
        _sessions = sessions;
        _library = library;
        store.DockChanged += (_, _) => OnUi(Reload);
        store.ArtworkChanged += (_, _) => OnUi(Reload);
        store.Changed += (_, _) => OnUi(Reload); // nom modifié, jeu retiré…
        sessions.SessionStarted += (_, _) => OnUi(RefreshPlaying);
        sessions.SessionEnded += (_, _) => OnUi(RefreshPlaying);
        Reload();
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    private Task Launch(DockItemViewModel item) => _library.PlayAsync(item.Id);

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

    private void Reload()
    {
        var dock = _store.GetDock();
        var playing = _sessions.Current?.Profile.Id;
        if (dock.Count == Items.Count && dock.Zip(Items).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name && p.First.CoverImageId == p.Second.CoverImageId))
        {
            RefreshPlaying(); // rien de visible n'a changé : on garde les éléments (et les animations en cours)
            return;
        }

        Items.Clear();
        foreach (var profile in dock)
        {
            var item = new DockItemViewModel(profile, playing == profile.Id)
            {
                CoverPath = _artwork.TryGetCached(profile.CoverImageId, Igdb.CoverSize),
            };
            Items.Add(item);
            if (item.CoverPath is null && profile.CoverImageId is not null)
            {
                _ = LoadCoverAsync(item, profile.CoverImageId);
            }
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadCoverAsync(DockItemViewModel item, string imageId) =>
        item.CoverPath = await _artwork.GetAsync(imageId, Igdb.CoverSize);

    private void RefreshPlaying()
    {
        var playing = _sessions.Current?.Profile.Id;
        foreach (var item in Items) item.IsPlaying = item.Id == playing;
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}

public sealed partial class DockItemViewModel(GameProfile profile, bool isPlaying) : ObservableObject
{
    public Guid Id { get; } = profile.Id;

    public string Name { get; } = profile.Name;

    public string? CoverImageId { get; } = profile.CoverImageId;

    public string Initials { get; } = string.Concat(profile.Name
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverPath;

    public bool HasCover => CoverPath is not null;

    [ObservableProperty]
    private bool _isPlaying = isPlaying;
}
