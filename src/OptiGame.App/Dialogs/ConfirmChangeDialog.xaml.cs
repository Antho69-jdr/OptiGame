using System.Windows;
using OptiGame.App.Controls;
using OptiGame.Core.Changes;
using OptiGame.Core.Profiles;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Confirmation explicite avant toute optimisation : ce qui change (titre), pourquoi, avertissement éventuel, droits
/// administrateur et redémarrage, où la restaurer, détails techniques repliés. « Annuler » est le bouton par défaut et reçoit
/// le focus. Pour une option avancée, il faut en plus cocher la case de prise de connaissance (double confirmation).
/// </summary>
public partial class ConfirmChangeDialog : DialogWindow
{
    private readonly bool _requiresAcknowledge;

    public ConfirmChangeDialog(ReversibleChange change, bool isAdvanced)
    {
        InitializeComponent();

        Layout.Heading = FrenchText.Typeset(isAdvanced ? $"Option avancée : {change.Title}" : change.Title);
        Layout.Icon = isAdvanced ? DialogIcon.Warning : DialogIcon.None;
        WhyText.Text = FrenchText.Typeset(change.Why);
        WhatText.Text = change.What;
        WarningBar.Message = FrenchText.Typeset(change.Warning);
        WarningBar.Visibility = change.Warning is null ? Visibility.Collapsed : Visibility.Visible;
        Requirements.RequiresAdmin = change.RequiresAdmin;
        Requirements.RequiresReboot = change.RequiresReboot;
        Requirements.Visibility = change.RequiresAdmin || change.RequiresReboot ? Visibility.Visible : Visibility.Collapsed;
        UndoText.Text = FrenchText.Typeset("Le réglage d'origine est sauvegardé avant la modification : vous pourrez le restaurer " +
            (GameChanges.IsGameChange(change.Id) ? "depuis la fiche du jeu." : "depuis la page Diagnostic."));

        _requiresAcknowledge = isAdvanced;
        AcknowledgeBox.Visibility = isAdvanced ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !isAdvanced;
        InitialFocus = CancelButton;
    }

    private void OnAcknowledgeChanged(object sender, RoutedEventArgs e) =>
        ApplyButton.IsEnabled = !_requiresAcknowledge || AcknowledgeBox.IsChecked == true;

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
