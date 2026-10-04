using System.Windows;
using OptiGame.Core.Changes;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Confirmation de plusieurs modifications en une fois (vue « Simple » du Diagnostic) : chacune est listée avec ce qu'elle
/// change et pourquoi, comme dans <see cref="ConfirmChangeDialog"/>. Le bouton par défaut est « Annuler ».
/// </summary>
public partial class ConfirmChangesDialog : Window
{
    public sealed record Row(string Title, string What, string Why, string Badges);

    public ConfirmChangesDialog(IReadOnlyList<ReversibleChange> changes)
    {
        InitializeComponent();
        TitleText.Text = changes.Count == 1 ? "Appliquer 1 optimisation" : $"Appliquer {changes.Count} optimisations";
        ApplyButton.Content = changes.Count == 1 ? "Appliquer" : $"Appliquer les {changes.Count}";
        ChangesList.ItemsSource = changes.Select(c => new Row(c.Title, c.What, c.Why, string.Join(" · ", new[]
        {
            c.RequiresAdmin ? "admin" : null,
            c.RequiresReboot ? "redémarrage" : null,
        }.OfType<string>()))).ToList();
        AdminText.Visibility = changes.Any(c => c.RequiresAdmin) ? Visibility.Visible : Visibility.Collapsed;
        RebootText.Visibility = changes.Any(c => c.RequiresReboot) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
