using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OptiGame.App.Views;

public partial class GamePageView : UserControl
{
    public GamePageView()
    {
        InitializeComponent();
    }

    /// <summary>Bouton « … » (Plus d'actions) : menu sous le bouton, au clic comme au clavier (Entrée, Espace).</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
