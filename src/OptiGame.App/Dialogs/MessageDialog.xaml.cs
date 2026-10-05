using System.Runtime.InteropServices;
using System.Windows;
using OptiGame.App.Controls;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Dialogue thémé qui remplace la MessageBox de Windows : question (bouton nommé par le verbe de l'action, « Annuler » par
/// défaut pour Entrée et Échap et focalisé), information ou erreur (un seul bouton « OK »), avec un message technique
/// facultatif replié dans « Détails techniques » (copiable).
/// </summary>
public partial class MessageDialog : DialogWindow
{
    private MessageDialog(DialogIcon icon, string heading, string message, string? details)
    {
        InitializeComponent();
        Layout.Icon = icon;
        Layout.Heading = FrenchText.Typeset(heading);
        MessageText.Text = FrenchText.Typeset(message);
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsText.Text = details.Trim();
            DetailsExpander.Visibility = Visibility.Visible;
        }
        InitialFocus = CancelButton;
    }

    /// <summary>Question : vrai seulement si l'utilisateur clique sur le bouton d'action.</summary>
    public static MessageDialog Question(string heading, string message, string confirmLabel, bool isDestructive, string cancelLabel = "Annuler")
    {
        var dialog = new MessageDialog(isDestructive ? DialogIcon.Warning : DialogIcon.None, heading, message, null);
        dialog.ConfirmButton.Content = confirmLabel;
        dialog.CancelButton.Content = cancelLabel;
        dialog.ConfirmButton.SetResourceReference(StyleProperty, isDestructive ? "Button.DangerFilled" : "Button.Primary");
        return dialog;
    }

    /// <summary>Information ou erreur : un seul bouton « OK ».</summary>
    public static MessageDialog Notice(DialogIcon icon, string heading, string message, string? details)
    {
        var dialog = new MessageDialog(icon, heading, message, details);
        dialog.ConfirmButton.Visibility = Visibility.Collapsed;
        dialog.CancelButton.Content = "OK";
        dialog.CancelButton.SetResourceReference(StyleProperty, "Button.Primary");
        return dialog;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DetailsText.Text);
        }
        catch (COMException)
        {
            // Presse-papiers occupé par une autre application : le texte reste sélectionnable dans le champ.
        }
    }
}
