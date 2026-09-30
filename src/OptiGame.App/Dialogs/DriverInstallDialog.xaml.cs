using System.Windows;
using OptiGame.Core.Drivers;

namespace OptiGame.App.Dialogs;

/// <summary>Choix fait dans la confirmation d'installation d'un pilote.</summary>
public sealed record DriverInstallChoice(bool CreateRestorePoint);

/// <summary>
/// Confirmation avant l'installation d'un pilote : ce qui est installé, pourquoi, et le fait qu'OptiGame ne pourra pas
/// l'annuler (exception au principe de réversibilité), avec le chemin du retour en arrière. Double confirmation :
/// case « je comprends » obligatoire ; le bouton par défaut est « Annuler ».
/// </summary>
public partial class DriverInstallDialog : Window
{
    public DriverInstallDialog(DriverInstallPlan plan)
    {
        InitializeComponent();

        TitleText.Text = plan.Title;
        WhatText.Text = plan.What;
        WhyText.Text = plan.Why;
        NotReversibleText.Text = plan.NotReversible;
        RollbackText.Text = plan.Rollback;
        RebootText.Visibility = plan.MayRequireReboot ? Visibility.Visible : Visibility.Collapsed;

        var canRestore = plan.RestorePoint == RestorePointAvailability.Available;
        RestorePointLabel.Text = DriverInstallPlans.RestorePointText(plan.RestorePoint);
        RestorePointDisabledText.Text = DriverInstallPlans.RestorePointText(plan.RestorePoint);
        RestorePointBox.Visibility = canRestore ? Visibility.Visible : Visibility.Collapsed;
        RestorePointDisabledText.Visibility = canRestore ? Visibility.Collapsed : Visibility.Visible;
    }

    public DriverInstallChoice? Choice { get; private set; }

    private void OnAcknowledgeChanged(object sender, RoutedEventArgs e) => InstallButton.IsEnabled = AcknowledgeBox.IsChecked == true;

    private void OnInstall(object sender, RoutedEventArgs e)
    {
        Choice = new DriverInstallChoice(RestorePointBox.Visibility == Visibility.Visible && RestorePointBox.IsChecked == true);
        DialogResult = true;
    }
}
