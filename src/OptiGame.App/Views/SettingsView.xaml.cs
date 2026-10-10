using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void Attach(SettingsViewModel? vm)
    {
        if (_attached is not null)
        {
            _attached.SecretSaved -= OnSecretSaved;
            _attached.DockGameMoved -= OnDockGameMoved;
        }
        _attached = vm;
        if (_attached is not null)
        {
            _attached.SecretSaved += OnSecretSaved;
            _attached.DockGameMoved += OnDockGameMoved;
        }
    }

    // Saisie du raccourci du micro (onglet Audio) : la touche pressée (avec Ctrl / Alt / Maj) va au ViewModel de l'appel, pas au bouton.
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Call: { IsCapturingMicKey: true } call }) return;
        var key = e.Key switch
        {
            System.Windows.Input.Key.System => e.SystemKey,
            System.Windows.Input.Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        call.CaptureMicKey(System.Windows.Input.KeyInterop.VirtualKeyFromKey(key), modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control),
            modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt), modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift));
        e.Handled = true;
        if (!call.IsCapturingMicKey) ChooseKeyButton.Focus();
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

    /// <summary>
    /// Jeu monté ou descendu : la liste est reconstruite (le focus serait perdu). Une fois la nouvelle ligne affichée, le focus
    /// revient sur le même bouton, ou sur l'autre s'il est maintenant grisé (jeu arrivé en tête ou en fin de liste).
    /// </summary>
    private void OnDockGameMoved(object? sender, DockGameMove move) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        var entry = DockGamesList.Items.OfType<DockGameEntry>().FirstOrDefault(e => e.Id == move.Id);
        if (entry is null || DockGamesList.ItemContainerGenerator.ContainerFromItem(entry) is not ContentPresenter row) return;
        row.ApplyTemplate();
        var template = row.ContentTemplate;
        if (template?.FindName("UpButton", row) is not UIElement up || template.FindName("DownButton", row) is not UIElement down) return;
        var preferred = move.Delta < 0 ? up : down;
        (preferred.IsEnabled ? preferred : preferred == up ? down : up).Focus();
    });
}
