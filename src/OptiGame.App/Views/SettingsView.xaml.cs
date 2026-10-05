using System.Windows;
using System.Windows.Controls;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _attached;

    public SettingsView()
    {
        InitializeComponent();
        // Abonné seulement tant que la vue est affichée (le ViewModel, unique, ne doit pas garder en vie une vue retirée).
        Loaded += (_, _) => Attach(DataContext as SettingsViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(SettingsViewModel? vm)
    {
        if (_attached is not null) _attached.SecretSaved -= OnSecretSaved;
        _attached = vm;
        if (_attached is not null) _attached.SecretSaved += OnSecretSaved;
    }

    /// <summary>Secret enregistré (chiffré) : le champ est vidé, l'indication « un secret est enregistré » prend le relais.</summary>
    private void OnSecretSaved(object? sender, EventArgs e) => SecretBox.Clear();

    /// <summary>PasswordBox ne se lie pas en XAML (par conception) : on transmet la saisie au ViewModel.</summary>
    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.IgdbClientSecret = SecretBox.Password;
        }
    }
}
