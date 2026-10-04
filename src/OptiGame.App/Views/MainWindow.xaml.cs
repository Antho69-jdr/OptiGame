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

        // Jaquettes décodées à la taille réelle de l'écran de la fenêtre (PerMonitorV2 : change d'un écran à l'autre).
        SourceInitialized += (_, _) => Converters.ImageLoader.DisplayScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        DpiChanged += (_, args) => Converters.ImageLoader.DisplayScale = args.NewDpi.DpiScaleX;

        // Fenêtre masquée : les jaquettes chargées (jusqu'à plusieurs centaines de Mo) sont libérées. Un seul nettoyage, une fois
        // l'affichage mis à jour : sans allocation (appli au repos pendant une partie), rien ne le déclencherait.
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) return;
            viewModel.Library.ReleaseCovers();
            Dispatcher.BeginInvoke(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        };

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
