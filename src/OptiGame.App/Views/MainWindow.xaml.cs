using System.ComponentModel;
using System.Windows;
using OptiGame.App.Services;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class MainWindow : Window
{
    /// <summary>En dessous de cette largeur (unités WPF), la barre latérale n'affiche plus que les icônes (comme NavigationView).</summary>
    public const double CompactNavigationWidth = 1008;

    public static readonly DependencyProperty IsNavigationCompactProperty = DependencyProperty.Register(
        nameof(IsNavigationCompact), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private bool _closingForGame;

    public MainWindow(MainViewModel viewModel, MemoryRelief relief)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Jaquettes décodées à la taille réelle de l'écran de la fenêtre (PerMonitorV2 : change d'un écran à l'autre).
        SourceInitialized += (_, _) => Converters.ImageLoader.DisplayScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        DpiChanged += (_, args) => Converters.ImageLoader.DisplayScale = args.NewDpi.DpiScaleX;

        // Bouton « précédent » de la souris : comme Alt+← (fiche du jeu → grille).
        PreviewMouseUp += (_, args) =>
        {
            if (args.ChangedButton != System.Windows.Input.MouseButton.XButton1) return;
            viewModel.BackCommand.Execute(null);
            args.Handled = true;
        };

        // Barre latérale compacte quand la fenêtre est étroite : état dérivé de la largeur, rien à mémoriser.
        SizeChanged += (_, args) => IsNavigationCompact = args.NewSize.Width < CompactNavigationWidth;

        // Fenêtre masquée ou fermée : les jaquettes chargées (jusqu'à plusieurs centaines de Mo) sont libérées.
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not true) relief.Release();
        };

        // Première ouverture : lancer l'analyse (lecture seule). Fenêtre recréée après une partie : déjà faite, ou en cours.
        Loaded += (_, _) =>
        {
            if (!viewModel.Diagnostic.HasResults && viewModel.Diagnostic.RunCommand.CanExecute(null))
            {
                viewModel.Diagnostic.RunCommand.Execute(null);
            }
        };
    }

    /// <summary>Barre latérale réduite aux icônes (fenêtre de moins de <see cref="CompactNavigationWidth"/> de large).</summary>
    public bool IsNavigationCompact
    {
        get => (bool)GetValue(IsNavigationCompactProperty);
        set => SetValue(IsNavigationCompactProperty, value);
    }

    /// <summary>Fermeture réelle au début d'une partie (pas un masquage par l'utilisateur).</summary>
    public bool IsClosingForGame => _closingForGame;

    /// <summary>Juste avant que la fenêtre se masque ou se ferme : sa place peut encore être lue (RestoreBounds).</summary>
    public event EventHandler? PlacementSaving;

    /// <summary>Fermeture réelle au début d'une partie (App.CloseMainWindowForGame) : tout le contenu est libéré.</summary>
    public void CloseForGame()
    {
        _closingForGame = true;
        Close();
    }

    /// <summary>Fermer la fenêtre la masque : l'appli reste dans la zone de notification (sauf fermeture pour une partie).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        PlacementSaving?.Invoke(this, EventArgs.Empty);
        if (!_closingForGame)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
