using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Controls;
using OptiGame.Core;
using OptiGame.Core.Logging;
using OptiGame.Core.Text;

namespace OptiGame.App.Services;

/// <summary>Une alerte de la coque : InfoBar en haut de chaque page, jusqu'à ce que l'utilisateur la ferme (ou que la cause disparaisse).</summary>
public sealed class ShellAlert(string key, Severity severity, string title, string message)
{
    /// <summary>Une seule alerte par clé : une nouvelle remplace l'ancienne (ex. « detection »).</summary>
    public string Key { get; } = key;

    public Severity Severity { get; } = severity;

    public string Title { get; } = FrenchText.Typeset(title);

    public string Message { get; } = FrenchText.Typeset(message);

    /// <summary>Faux pour une panne en cours (elle disparaît d'elle-même quand la cause disparaît).</summary>
    public bool IsClosable { get; init; } = true;

    /// <summary>Bouton « Ouvrir le journal » (erreurs).</summary>
    public bool ShowsLog { get; init; }

    /// <summary>Action propre à l'alerte (ex. « Réessayer »), facultative.</summary>
    public string? ActionLabel { get; init; }

    public ICommand? ActionCommand { get; init; }

    public bool HasAction => ActionLabel is not null && ActionCommand is not null;
}

/// <summary>
/// Alertes persistantes de la fenêtre (principe : les erreurs importantes s'affichent DANS la fenêtre, en plus des
/// notifications Windows, masquables sous AtlasOS, et du journal). Gardées pendant que la fenêtre est fermée : elles
/// s'affichent à sa réouverture.
/// </summary>
public sealed partial class ShellAlerts(AppPaths paths, FileLog log) : ObservableObject
{
    public ObservableCollection<ShellAlert> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public void Show(ShellAlert alert) => OnUi(() =>
    {
        Remove(alert.Key);
        // Les erreurs d'abord, puis les avertissements, puis le reste : la plus grave reste en haut.
        var index = Items.TakeWhile(a => Rank(a.Severity) >= Rank(alert.Severity)).Count();
        Items.Insert(index, alert);
        OnPropertyChanged(nameof(HasItems));
    });

    public void Dismiss(string key) => OnUi(() =>
    {
        Remove(key);
        OnPropertyChanged(nameof(HasItems));
    });

    [RelayCommand]
    private void Close(ShellAlert? alert)
    {
        if (alert is not null) Dismiss(alert.Key);
    }

    /// <summary>Journal d'OptiGame, ouvert par l'Explorateur (éditeur de texte de l'utilisateur, sans droits administrateur).</summary>
    [RelayCommand]
    private void OpenLog()
    {
        var file = Path.Combine(paths.LogsDir, "optigame.log");
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{(File.Exists(file) ? file : paths.LogsDir)}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Error("Ouverture du journal impossible", ex);
        }
    }

    private static int Rank(Severity severity) => severity switch
    {
        Severity.Error => 3,
        Severity.Warning => 2,
        Severity.Success => 1,
        _ => 0,
    };

    private void Remove(string key)
    {
        if (Items.FirstOrDefault(a => a.Key == key) is { } existing) Items.Remove(existing);
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
