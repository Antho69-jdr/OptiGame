using System.Windows;
using System.Windows.Controls;
using OptiGame.Core.Library;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Jeux installés détectés (Steam, Epic Games, GOG, dossiers de jeux) : liste à cocher accessible au clavier, choix de l'exe
/// par jeu, jeux déjà dans Mes jeux grisés. Le bouton dit combien de jeux seront ajoutés ; inactif tant que rien n'est coché.
/// </summary>
public partial class GameScanDialog : DialogWindow
{
    private readonly List<Item> _items;

    public GameScanDialog(IReadOnlyList<InstalledGame> games, Func<string, bool> hasProfile, IReadOnlyList<string> gameFolders)
    {
        InitializeComponent();
        _items = games.Select(g => new Item(g, hasProfile)).ToList();
        List.ItemsSource = _items;
        FoldersHint.Text = FrenchText.Typeset(gameFolders.Count == 0
            ? "Recherche dans Steam, Epic Games et GOG. Pour inclure un autre dossier de jeux (ex. A:\\Jeux), ajoutez-le dans Paramètres."
            : $"Recherche dans Steam, Epic Games, GOG et dans : {string.Join(", ", gameFolders)}.");
        UpdateButtons();
        InitialFocus = List;
    }

    public IReadOnlyList<(InstalledGame Game, ExeFile Exe)> Selection =>
        List.SelectedItems.OfType<Item>().Where(i => i.SelectedExe is not null).Select(i => (i.Game, i.SelectedExe!.Exe)).ToList();

    private int SelectableCount => _items.Count(i => i.CanSelect);

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItems.Count == SelectableCount)
        {
            List.UnselectAll();
            return;
        }
        foreach (var item in _items.Where(i => i.CanSelect && !List.SelectedItems.Contains(i)))
        {
            List.SelectedItems.Add(item);
        }
    }

    private void UpdateButtons()
    {
        var count = List.SelectedItems.Count;
        AddButton.Content = count == 0 ? "Ajouter" : $"Ajouter {FrenchText.Count(count, "jeu", "jeux")}";
        AddButton.IsEnabled = count > 0;
        SelectAllButton.Content = count == SelectableCount && count > 0 ? "Tout décocher" : "Tout cocher";
        SelectAllButton.Visibility = SelectableCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed class Item
    {
        public Item(InstalledGame game, Func<string, bool> hasProfile)
        {
            Game = game;
            Candidates = game.Candidates.Select(c => new CandidateItem(c, game.Folder)).ToList();
            SelectedExe = Candidates.FirstOrDefault();
            HasProfile = game.Candidates.Any(c => hasProfile(c.Path));
        }

        public InstalledGame Game { get; }

        public string Name => Game.Name;

        private bool HasProfile { get; }

        public bool CanSelect => !HasProfile;

        private string Source => Game.Source switch
        {
            GameSource.Steam => "Steam",
            GameSource.Epic => "Epic Games",
            GameSource.Gog => "GOG",
            _ => Game.Folder,
        };

        public string Suffix => $"  — {Source}" + (HasProfile ? " · déjà dans Mes jeux" : "");

        public string AccessibleName => $"{Name}, {Source}" + (HasProfile ? ", déjà dans Mes jeux" : "");

        public IReadOnlyList<CandidateItem> Candidates { get; }

        public CandidateItem? SelectedExe { get; set; }

        public override string ToString() => AccessibleName;
    }

    private sealed class CandidateItem(ExeFile exe, string folder)
    {
        public ExeFile Exe { get; } = exe;

        public string Label => $"{Path.GetRelativePath(folder, Exe.Path)}   ({Exe.SizeBytes / (1024.0 * 1024):N1} Mo)";

        public override string ToString() => Label;
    }
}
