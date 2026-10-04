using System.Windows.Controls;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    /// <summary>Jeux non installés : la page suivante s'ajoute quand il reste moins d'un écran à faire défiler.</summary>
    private void OnGridScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || e.OriginalSource != scroll || DataContext is not LibraryViewModel { IsUninstalledSectionVisible: true, HasMoreUninstalled: true } library) return;
        if (scroll.VerticalOffset + 2 * scroll.ViewportHeight >= scroll.ExtentHeight)
        {
            library.ShowMoreUninstalledCommand.Execute(null);
        }
    }
}
