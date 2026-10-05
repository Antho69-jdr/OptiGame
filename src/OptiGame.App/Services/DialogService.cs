using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using OptiGame.App.Dialogs;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Artwork;
using OptiGame.Core.Changes;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.State;

namespace OptiGame.App.Services;

public interface IDialogService
{
    /// <summary>Affiche ce que le changement modifie et pourquoi. Renvoie vrai uniquement si l'utilisateur confirme.</summary>
    bool ConfirmChange(ReversibleChange change, bool isAdvanced);

    /// <summary>Confirmation de plusieurs modifications en une fois (chacune listée).</summary>
    bool ConfirmChanges(IReadOnlyList<ReversibleChange> changes);

    /// <summary>« Restaurer le réglage d'origine ? » pour une optimisation active ; vrai si l'utilisateur confirme.</summary>
    bool ConfirmUndo(ChangeRecord change);

    /// <summary>Confirmation d'une installation de pilote (non annulable par OptiGame) ; null si annulée.</summary>
    Dialogs.DriverInstallChoice? ConfirmDriverInstall(Core.Drivers.DriverInstallPlan plan);

    /// <summary>
    /// Question : <paramref name="heading"/> = ce que l'on décide, <paramref name="confirmLabel"/> = verbe de l'action (jamais
    /// « Oui »). « Annuler » est le choix par défaut (Entrée, Échap). Destructrice = bouton rouge et icône d'avertissement.
    /// </summary>
    bool Confirm(string heading, string message, string confirmLabel, bool isDestructive = false, string cancelLabel = "Annuler");

    void ShowInfo(string heading, string message = "");

    /// <summary>
    /// Erreur : <paramref name="heading"/> = ce qui a échoué, <paramref name="message"/> = conséquence et suite (en clair),
    /// <paramref name="details"/> = message technique (exception), replié et copiable. Toujours écrite au journal.
    /// </summary>
    void ShowError(string heading, string message, string? details = null);

    /// <summary>Sélection d'un exécutable de jeu ; null si annulé.</summary>
    string? PickExecutable(string? initialPath);

    /// <summary>Sélection de programmes ouverts ; renvoie les noms d'exe choisis (vide si annulé).</summary>
    IReadOnlyList<string> PickRunningPrograms(IReadOnlyList<RunningProgram> programs);

    /// <summary>Sélection de jeux installés et de leur exe ; vide si annulé.</summary>
    IReadOnlyList<(InstalledGame Game, ExeFile Exe)> PickInstalledGames(
        IReadOnlyList<InstalledGame> games, Func<string, bool> hasProfile, IReadOnlyList<string> gameFolders);

    /// <summary>Sélection d'un dossier ; null si annulé.</summary>
    string? PickFolder(string title);

    /// <summary>Sélection d'un fichier .exe quelconque ; null si annulé.</summary>
    string? PickProgram(string title, string? initialPath);

    /// <summary>Recherche IGDB avec aperçus ; renvoie le jeu choisi ou null.</summary>
    IgdbGame? PickIgdbGame(string initialQuery, Func<string, Task<IReadOnlyList<IgdbGame>>> search, Func<string, Task<string?>> loadThumbnail);

    /// <summary>« Changer le fond… » : renvoie le fond choisi, ou null si l'utilisateur annule.</summary>
    Dialogs.BackgroundChoice? PickBackground(string gameName, string? steamHeroPath, Func<Task<IReadOnlyList<IgdbBackground>>>? loadIgdb,
        Func<string, Task<string?>> loadThumbnail, bool hasCustomBackground);
}

public sealed class DialogService(FileLog log) : IDialogService
{
    public bool ConfirmChanges(IReadOnlyList<ReversibleChange> changes) =>
        ShowOwned(new Dialogs.ConfirmChangesDialog(changes)) == true;

    public bool ConfirmChange(ReversibleChange change, bool isAdvanced) =>
        ShowOwned(new ConfirmChangeDialog(change, isAdvanced)) == true;

    public Dialogs.DriverInstallChoice? ConfirmDriverInstall(Core.Drivers.DriverInstallPlan plan)
    {
        var dialog = new Dialogs.DriverInstallDialog(plan);
        return ShowOwned(dialog) == true ? dialog.Choice : null;
    }

    public bool ConfirmUndo(ChangeRecord change) =>
        Confirm("Restaurer le réglage d'origine ?",
            $"OptiGame rétablit le réglage sauvegardé avant l'optimisation « {change.Title} »." +
            (change.RequiresReboot ? "\n\nRedémarrez ensuite Windows pour qu'il soit pris en compte." : ""),
            "Restaurer l'original");

    public bool Confirm(string heading, string message, string confirmLabel, bool isDestructive = false, string cancelLabel = "Annuler") =>
        ShowOwned(MessageDialog.Question(heading, message, confirmLabel, isDestructive, cancelLabel)) == true;

    public void ShowInfo(string heading, string message = "") =>
        ShowOwned(MessageDialog.Notice(Controls.DialogIcon.Info, heading, message, null));

    public void ShowError(string heading, string message, string? details = null)
    {
        log.Warn($"Erreur affichée : {heading} — {message}".Replace("\n", " ") + (details is null ? "" : $" [{details.Replace("\n", " ")}]"));
        ShowOwned(MessageDialog.Notice(Controls.DialogIcon.Error, heading, message, details));
    }

    public string? PickExecutable(string? initialPath) => PickProgram("Choisir l'exécutable du jeu", initialPath);

    public string? PickProgram(string title, string? initialPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
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

    public IReadOnlyList<(InstalledGame Game, ExeFile Exe)> PickInstalledGames(
        IReadOnlyList<InstalledGame> games, Func<string, bool> hasProfile, IReadOnlyList<string> gameFolders)
    {
        var dialog = new GameScanDialog(games, hasProfile, gameFolders);
        return ShowOwned(dialog) == true ? dialog.Selection : [];
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FolderName : null;
    }

    public IgdbGame? PickIgdbGame(string initialQuery, Func<string, Task<IReadOnlyList<IgdbGame>>> search, Func<string, Task<string?>> loadThumbnail)
    {
        var dialog = new IgdbSearchDialog(initialQuery, search, loadThumbnail);
        return ShowOwned(dialog) == true ? dialog.Selected : null;
    }

    public Dialogs.BackgroundChoice? PickBackground(string gameName, string? steamHeroPath, Func<Task<IReadOnlyList<IgdbBackground>>>? loadIgdb,
        Func<string, Task<string?>> loadThumbnail, bool hasCustomBackground)
    {
        var dialog = new Dialogs.BackgroundPickerDialog(gameName, steamHeroPath, loadIgdb, loadThumbnail, hasCustomBackground);
        return ShowOwned(dialog) == true ? dialog.Selected : null;
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
