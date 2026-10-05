using System.Windows;
using OptiGame.Core.Drivers;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>Choix fait dans la confirmation d'installation d'un pilote.</summary>
public sealed record DriverInstallChoice(bool CreateRestorePoint);

/// <summary>
/// Confirmation avant l'installation d'un pilote : le fait qu'OptiGame ne pourra pas l'annuler (exception au principe de
/// réversibilité) et le chemin du retour en arrière, en premier ; puis ce qui est installé et pourquoi, le point de
/// restauration. Double confirmation : case « je comprends » obligatoire ; « Annuler » est le bouton par défaut et a le focus.
/// </summary>
public partial class DriverInstallDialog : DialogWindow
{
    public DriverInstallDialog(DriverInstallPlan plan)
    {
        InitializeComponent();

        Layout.Heading = FrenchText.Typeset(plan.Title + " ?");
        NotReversibleBar.Title = FrenchText.Typeset(plan.NotReversible);
        NotReversibleBar.Message = FrenchText.Typeset(plan.Rollback);
        WhatText.Text = plan.What;
        WhyText.Text = FrenchText.Typeset(plan.Why);
        Requirements.RequiresReboot = plan.MayRequireReboot;

        var canRestore = plan.RestorePoint == RestorePointAvailability.Available;
        var restoreText = FrenchText.Typeset(DriverInstallPlans.RestorePointText(plan.RestorePoint));
        RestorePointLabel.Text = restoreText;
        RestorePointDisabledText.Text = restoreText;
        RestorePointBox.Visibility = canRestore ? Visibility.Visible : Visibility.Collapsed;
        RestorePointDisabledText.Visibility = canRestore ? Visibility.Collapsed : Visibility.Visible;
        InitialFocus = CancelButton;
    }

    public DriverInstallChoice? Choice { get; private set; }

    private void OnAcknowledgeChanged(object sender, RoutedEventArgs e) => InstallButton.IsEnabled = AcknowledgeBox.IsChecked == true;

    private void OnInstall(object sender, RoutedEventArgs e)
    {
        Choice = new DriverInstallChoice(RestorePointBox.Visibility == Visibility.Visible && RestorePointBox.IsChecked == true);
        DialogResult = true;
    }
}
