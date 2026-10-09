using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class CallView : UserControl
{
    public CallView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // Saisie du raccourci du micro : la touche pressée (avec Ctrl / Alt / Maj) est donnée au ViewModel, et pas au bouton.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CallViewModel { IsCapturingMicKey: true } call) return;
        var key = e.Key switch
        {
            Key.System => e.SystemKey, // touches avec Alt, et F10
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        var modifiers = Keyboard.Modifiers;
        call.CaptureMicKey(KeyInterop.VirtualKeyFromKey(key), modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Alt),
            modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
        if (!call.IsCapturingMicKey) ChooseKeyButton.Focus(); // saisie finie : le focus revient au bouton
    }
}
