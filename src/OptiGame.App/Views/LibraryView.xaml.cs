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

    private System.Windows.Window? _window;

    private void Attach(LibraryViewModel? library)
    {
        if (_library is not null)
        {
            _library.UninstalledListReset -= OnUninstalledListReset;
            _library.SearchFocusRequested -= OnSearchFocusRequested;
        }
        if (_window is not null) _window.Activated -= OnWindowActivated;
        _library = library;
        _window = library is null ? null : System.Windows.Window.GetWindow(this);
        if (_library is not null)
        {
            _library.UninstalledListReset += OnUninstalledListReset;
            _library.SearchFocusRequested += OnSearchFocusRequested;
            _ = _library.Launchers.RefreshAsync(); // lanceurs ouverts ou fermés entre-temps
        }
        if (_window is not null) _window.Activated += OnWindowActivated;
    }

    /// <summary>Retour sur la fenêtre (un lanceur a pu être ouvert ou fermé ailleurs) : état des lanceurs relu, sans boucle.</summary>
    private void OnWindowActivated(object? sender, EventArgs e) => _ = _library?.Launchers.RefreshAsync();

    /// <summary>Bouton d'un lanceur : menu Ouvrir / Fermer… sous le bouton (clic, Entrée ou Espace).</summary>
    private void OnLauncherClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.DataContext = button.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Ctrl+F : curseur dans la recherche, texte sélectionné (une frappe le remplace).</summary>
    private void OnSearchFocusRequested(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }, System.Windows.Threading.DispatcherPriority.Input);

    private void OnUninstalledListReset(object? sender, EventArgs e) => GridScroll.ScrollToTop();

    /// <summary>« Ajouter des jeux » : menu sous le bouton (clic, Entrée ou Espace) ; le premier choix reçoit le focus clavier.</summary>
    private void OnAddGamesClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (AddGamesButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = AddGamesButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

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
