using System.Windows.Controls;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class LibraryView : UserControl
{
    private LibraryViewModel? _library;

    public LibraryView()
    {
        InitializeComponent();
        // Abonné seulement tant que la vue est affichée : la bibliothèque (unique) ne doit pas garder en vie une vue retirée.
        Loaded += (_, _) => Attach(DataContext as LibraryViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(LibraryViewModel? library)
    {
        if (_library is not null)
        {
            _library.UninstalledListReset -= OnUninstalledListReset;
            _library.SearchFocusRequested -= OnSearchFocusRequested;
        }
        _library = library;
        if (_library is not null)
        {
            _library.UninstalledListReset += OnUninstalledListReset;
            _library.SearchFocusRequested += OnSearchFocusRequested;
        }
    }

    /// <summary>Ctrl+F : curseur dans la recherche, texte sélectionné (une frappe le remplace).</summary>
    private void OnSearchFocusRequested(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }, System.Windows.Threading.DispatcherPriority.Input);

    private void OnUninstalledListReset(object? sender, EventArgs e) => GridScroll.ScrollToTop();

    /// <summary>
    /// Jeux non installés : la page suivante s'ajoute quand il reste moins de deux écrans à faire défiler, mais seulement si l'on
    /// descend ou si le contenu ou la fenêtre grandit. Une liste ramenée à sa première page laisse le défilement au bas de la liste
    /// raccourcie, ce qui n'est pas une raison de tout recharger : mesuré le 2026-10-04, fenêtre masquée → 631 jaquettes
    /// rechargées une à une (WPF continue la mise en page d'une fenêtre masquée, d'où aussi le test IsVisible).
    /// </summary>
    private void OnGridScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || e.OriginalSource != scroll || !IsVisible) return;
        if (e.VerticalChange <= 0 && e.ExtentHeightChange <= 0 && e.ViewportHeightChange <= 0) return;
        if (DataContext is not LibraryViewModel { IsUninstalledSectionVisible: true, HasMoreUninstalled: true } library) return;
        if (scroll.VerticalOffset + 2 * scroll.ViewportHeight >= scroll.ExtentHeight)
        {
            library.ShowMoreUninstalledCommand.Execute(null);
        }
    }
}
