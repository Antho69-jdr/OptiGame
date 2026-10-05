using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OptiGame.Core.Artwork;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Recherche IGDB avec aperçu des jaquettes ; renvoie le jeu choisi. Entrée dans le champ = rechercher ; Entrée dans la liste
/// (ou double-clic) = utiliser la jaquette choisie.
/// </summary>
public partial class IgdbSearchDialog : DialogWindow
{
    private readonly Func<string, Task<IReadOnlyList<IgdbGame>>> _search;
    private readonly Func<string, Task<string?>> _loadThumbnail;
    private readonly ObservableCollection<Result> _results = [];
    private int _searchId;

    public IgdbSearchDialog(string initialQuery, Func<string, Task<IReadOnlyList<IgdbGame>>> search, Func<string, Task<string?>> loadThumbnail)
    {
        InitializeComponent();
        _search = search;
        _loadThumbnail = loadThumbnail;
        Results.ItemsSource = _results;
        QueryBox.Text = initialQuery;
        InitialFocus = QueryBox;
        Loaded += async (_, _) => await SearchAsync();
    }

    public IgdbGame? Selected { get; private set; }

    private async void OnSearch(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true; // sinon le bouton par défaut utiliserait la jaquette sélectionnée
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var id = ++_searchId;
        var query = QueryBox.Text.Trim();
        if (query.Length == 0) return;

        _results.Clear();
        UseButton.IsEnabled = false;
        StatusText.Text = "Recherche…";
        try
        {
            var games = (await _search(query)).Where(g => g.CoverImageId is not null).ToList();
            if (id != _searchId) return; // Une recherche plus récente a été lancée.
            foreach (var game in games) _results.Add(new Result(game));
            StatusText.Text = games.Count == 0
                ? "Aucun résultat avec jaquette. Essayez un autre nom (titre original, sans sous-titre)."
                : FrenchText.Typeset($"{FrenchText.Count(games.Count, "résultat", "résultats")} : choisissez une jaquette.");

            // Aperçus un par un (l'API d'images n'a pas la limite de 4 requêtes/s, mais inutile de tout lancer d'un coup).
            foreach (var result in _results.ToList())
            {
                if (id != _searchId) return;
                result.ThumbPath = await _loadThumbnail(result.Game.CoverImageId!);
                result.PreviewText = result.ThumbPath is null ? "Aperçu indisponible" : "";
            }
        }
        catch (Exception)
        {
            if (id == _searchId)
            {
                StatusText.Text = FrenchText.Typeset("Recherche impossible : vérifiez la connexion à Internet et vos identifiants IGDB dans Paramètres.");
            }
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UseButton.IsEnabled = Results.SelectedItem is not null;

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Results.SelectedItem is not null) OnUse(sender, e);
    }

    private void OnUse(object sender, RoutedEventArgs e)
    {
        Selected = (Results.SelectedItem as Result)?.Game;
        if (Selected is not null) DialogResult = true;
    }

    private sealed class Result(IgdbGame game) : INotifyPropertyChanged
    {
        public IgdbGame Game { get; } = game;

        public string Name => Game.Name;

        public string YearText => Game.Year?.ToString() ?? "";

        public string? ThumbPath
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Texte du remplaçant : « … » pendant le chargement, « Aperçu indisponible » en cas d'échec.</summary>
        public string PreviewText
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        } = "…";

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Game.Year is { } year ? $"{Name}, {year}" : Name;

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
