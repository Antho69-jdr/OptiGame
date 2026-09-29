using System.Windows;
using OptiGame.App.Dialogs;
using OptiGame.Core.Changes;
using OptiGame.Core.State;

namespace OptiGame.App.Services;

public interface IDialogService
{
    /// <summary>Affiche ce que le changement modifie et pourquoi. Renvoie vrai uniquement si l'utilisateur confirme.</summary>
    bool ConfirmChange(ReversibleChange change, bool isAdvanced);

    bool ConfirmUndo(ChangeRecord change);

    void ShowInfo(string message);

    void ShowError(string message);
}

public sealed class DialogService : IDialogService
{
    private const string Caption = "OptiGame";

    public bool ConfirmChange(ReversibleChange change, bool isAdvanced)
    {
        var dialog = new ConfirmChangeDialog(change, isAdvanced);
        if (ActiveWindow() is { } owner)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return dialog.ShowDialog() == true;
    }

    public bool ConfirmUndo(ChangeRecord change) =>
        Show($"Annuler « {change.Title} » ?\n\nLe réglage d'origine, sauvegardé avant la correction, sera restauré." +
             (change.RequiresReboot ? "\n\nUn redémarrage sera nécessaire pour que ce soit pris en compte." : ""),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowInfo(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public void ShowError(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult) =>
        ActiveWindow() is { } owner
            ? MessageBox.Show(owner, message, Caption, buttons, image, defaultResult)
            : MessageBox.Show(message, Caption, buttons, image, defaultResult);

    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? (Application.Current.MainWindow is { IsVisible: true } main ? main : null);
}
