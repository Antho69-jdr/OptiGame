using System.ComponentModel;
using System.Windows;
using OptiGame.App.Services;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class MainWindow : Window
{
    private bool _closingForGame;

    public MainWindow(MainViewModel viewModel, MemoryRelief relief)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Jaquettes décodées à la taille réelle de l'écran de la fenêtre (PerMonitorV2 : change d'un écran à l'autre).
        SourceInitialized += (_, _) => Converters.ImageLoader.DisplayScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        DpiChanged += (_, args) => Converters.ImageLoader.DisplayScale = args.NewDpi.DpiScaleX;

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

    /// <summary>Fermeture réelle au début d'une partie (App.CloseMainWindowForGame) : tout le contenu est libéré.</summary>
    public void CloseForGame()
    {
        _closingForGame = true;
        Close();
    }

    /// <summary>Fermer la fenêtre la masque : l'appli reste dans la zone de notification (sauf fermeture pour une partie).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingForGame)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
