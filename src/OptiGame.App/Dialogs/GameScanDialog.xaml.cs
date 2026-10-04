using System.Windows;
using OptiGame.Core.Library;

namespace OptiGame.App.Dialogs;

public partial class GameScanDialog : Window
{
    private readonly List<Item> _items;

    public GameScanDialog(IReadOnlyList<InstalledGame> games, Func<string, bool> hasProfile, IReadOnlyList<string> gameFolders)
    {
        InitializeComponent();
        _items = games.Select(g => new Item(g, hasProfile)).ToList();
        List.ItemsSource = _items;
        FoldersHint.Text = gameFolders.Count == 0
            ? "Recherche dans les bibliothèques Steam. Pour inclure d'autres dossiers de jeux (ex. A:\\Jeux), ajoutez-les dans l'onglet Paramètres."
            : $"Recherche dans les bibliothèques Steam et dans : {string.Join(", ", gameFolders)}.";
    }

    public IReadOnlyList<(InstalledGame Game, ExeFile Exe)> Selection =>
        _items.Where(i => i.IsSelected && i.SelectedExe is not null).Select(i => (i.Game, i.SelectedExe!.Exe)).ToList();

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

        public string Suffix =>
            Game.Source switch
            {
                GameSource.Steam => "  — Steam",
                GameSource.Epic => "  — Epic Games",
                GameSource.Gog => "  — GOG",
                _ => $"  — {Game.Folder}",
            } + (HasProfile ? "  (profil existant)" : "");

        public IReadOnlyList<CandidateItem> Candidates { get; }

        public CandidateItem? SelectedExe { get; set; }

        public bool IsSelected { get; set; }
    }

    private sealed class CandidateItem(ExeFile exe, string folder)
    {
        public ExeFile Exe { get; } = exe;

        public string Label => $"{Path.GetRelativePath(folder, Exe.Path)}   ({Exe.SizeBytes / (1024.0 * 1024):N1} Mo)";
    }
}
