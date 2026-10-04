using System.ComponentModel;
using System.Windows;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Première ouverture : lancer l'analyse (lecture seule).
        Loaded += (_, _) =>
        {
            if (!viewModel.Diagnostic.HasResults)
            {
                viewModel.Diagnostic.RunCommand.Execute(null);
            }
        };
    }

    /// <summary>Fermer la fenêtre la masque : l'appli reste dans la zone de notification.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
