using System.Windows;
using OptiGame.Core.Changes;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Confirmation de plusieurs optimisations en une fois (bouton unique du Diagnostic) : chacune est listée avec pourquoi,
/// ses exigences et ses détails techniques repliés, comme dans <see cref="ConfirmChangeDialog"/>. « Annuler » est le bouton
/// par défaut et reçoit le focus.
/// </summary>
public partial class ConfirmChangesDialog : DialogWindow
{
    public sealed record Row(string Title, string What, string Why, bool RequiresAdmin, bool RequiresReboot);

    public ConfirmChangesDialog(IReadOnlyList<ReversibleChange> changes)
    {
        InitializeComponent();
        Layout.Heading = changes.Count == 1 ? "Appliquer 1 optimisation ?" : $"Appliquer {changes.Count} optimisations ?";
        ApplyButton.Content = changes.Count == 1 ? "Appliquer l'optimisation" : $"Appliquer les {changes.Count} optimisations";
        ChangesList.ItemsSource = changes
            .Select(c => new Row(FrenchText.Typeset(c.Title), c.What, FrenchText.Typeset(c.Why), c.RequiresAdmin, c.RequiresReboot))
            .ToList();
        InitialFocus = CancelButton;
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
