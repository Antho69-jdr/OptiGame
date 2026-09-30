using System.Windows;
using System.Windows.Controls;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class DriversView : UserControl
{
    public DriversView()
    {
        InitializeComponent();
    }

    /// <summary>Première ouverture de la page : la recherche démarre toute seule (lecture seule).</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as DriversViewModel)?.EnsureSearched();
}
