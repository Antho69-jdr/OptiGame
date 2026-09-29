using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using OptiGame.App.Dialogs;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.State;

namespace OptiGame.App.Services;

public interface IDialogService
{
    /// <summary>Affiche ce que le changement modifie et pourquoi. Renvoie vrai uniquement si l'utilisateur confirme.</summary>
    bool ConfirmChange(ReversibleChange change, bool isAdvanced);

    bool ConfirmUndo(ChangeRecord change);

    /// <summary>Question oui/non ; « Non » par défaut.</summary>
    bool Confirm(string message);

    void ShowInfo(string message);

    void ShowError(string message);

    /// <summary>Sélection d'un exécutable de jeu ; null si annulé.</summary>
    string? PickExecutable(string? initialPath);

    /// <summary>Sélection de programmes ouverts ; renvoie les noms d'exe choisis (vide si annulé).</summary>
    IReadOnlyList<string> PickRunningPrograms(IReadOnlyList<RunningProgram> programs);
}

public sealed class DialogService : IDialogService
{
    private const string Caption = "OptiGame";

    public bool ConfirmChange(ReversibleChange change, bool isAdvanced) =>
        ShowOwned(new ConfirmChangeDialog(change, isAdvanced)) == true;

    public bool ConfirmUndo(ChangeRecord change) =>
        Confirm($"Annuler « {change.Title} » ?\n\nLe réglage d'origine, sauvegardé avant la correction, sera restauré." +
                (change.RequiresReboot ? "\n\nUn redémarrage sera nécessaire pour que ce soit pris en compte." : ""));

    public bool Confirm(string message) =>
        Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowInfo(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public void ShowError(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);

    public string? PickExecutable(string? initialPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir l'exécutable du jeu",
            Filter = "Programmes (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (initialPath is not null && Path.GetDirectoryName(initialPath) is { } dir && Directory.Exists(dir))
        {
            dialog.InitialDirectory = dir;
        }
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> PickRunningPrograms(IReadOnlyList<RunningProgram> programs)
    {
        var dialog = new ProcessPickerDialog(programs);
        return ShowOwned(dialog) == true ? dialog.SelectedExeNames : [];
    }

    private static bool? ShowOwned(Window dialog)
    {
        if (ActiveWindow() is { } owner)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return dialog.ShowDialog();
    }

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult) =>
        ActiveWindow() is { } owner
            ? MessageBox.Show(owner, message, Caption, buttons, image, defaultResult)
            : MessageBox.Show(message, Caption, buttons, image, defaultResult);

    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? (Application.Current.MainWindow is { IsVisible: true } main ? main : null);

    /// <summary>Nom lisible d'un exe (description du fichier), pour nommer un nouveau profil.</summary>
    public static string FriendlyName(string exePath)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(exePath).FileDescription?.Trim();
            if (!string.IsNullOrWhiteSpace(description)) return description;
        }
        catch (FileNotFoundException)
        {
        }
        return Path.GetFileNameWithoutExtension(exePath);
    }
}
