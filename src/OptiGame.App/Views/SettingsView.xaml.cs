using System.Windows;
using System.Windows.Controls;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>PasswordBox ne se lie pas en XAML (par conception) : on transmet la saisie au ViewModel.</summary>
    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.IgdbClientSecret = SecretBox.Password;
        }
    }
}
