using System.Windows;
using OptiGame.Core.Changes;

namespace OptiGame.App.Dialogs;

/// <summary>
/// Confirmation explicite avant toute modification. Le bouton par défaut est « Annuler ».
/// Pour une option avancée, il faut en plus cocher la case de prise de connaissance (double confirmation).
/// </summary>
public partial class ConfirmChangeDialog : Window
{
    private readonly bool _requiresAcknowledge;

    public ConfirmChangeDialog(ReversibleChange change, bool isAdvanced)
    {
        InitializeComponent();

        TitleText.Text = isAdvanced ? $"Option avancée : {change.Title}" : change.Title;
        WhatText.Text = change.What;
        WhyText.Text = change.Why;
        WarningText.Text = change.Warning ?? "";
        WarningPanel.Visibility = change.Warning is null ? Visibility.Collapsed : Visibility.Visible;
        AdminText.Visibility = change.RequiresAdmin ? Visibility.Visible : Visibility.Collapsed;
        RebootText.Visibility = change.RequiresReboot ? Visibility.Visible : Visibility.Collapsed;

        _requiresAcknowledge = isAdvanced;
        AcknowledgeBox.Visibility = isAdvanced ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !isAdvanced;
    }

    private void OnAcknowledgeChanged(object sender, RoutedEventArgs e) =>
        ApplyButton.IsEnabled = !_requiresAcknowledge || AcknowledgeBox.IsChecked == true;

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
