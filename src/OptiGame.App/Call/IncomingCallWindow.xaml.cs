using System.Windows;

namespace OptiGame.App.Call;

public partial class IncomingCallWindow : Window
{
    public IncomingCallWindow()
    {
        InitializeComponent();
    }

    /// <summary>En bas à droite de la zone de travail de l'écran principal, au-dessus de la barre des tâches.</summary>
    public void PlaceAtCorner()
    {
        var area = SystemParameters.WorkArea;
        UpdateLayout();
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
    }
}
