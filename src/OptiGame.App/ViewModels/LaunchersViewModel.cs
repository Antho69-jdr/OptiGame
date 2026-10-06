using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.Sessions;
using OptiGame.Platform.Processes;

namespace OptiGame.App.ViewModels;

/// <summary>
/// Barre « Lanceurs » de Mes jeux : un bouton par lanceur installé (Steam, Epic Games, GOG Galaxy), point vert s'il est ouvert ;
/// menu Ouvrir / Fermer…. État relu à l'affichage de Mes jeux, au retour sur la fenêtre et après chaque action (jamais en boucle).
/// </summary>
public sealed partial class LaunchersViewModel(LauncherControl control, IDialogService dialogs, GameSessionManager sessions, FileLog log) : ObservableObject
{
    public ObservableCollection<LauncherItemViewModel> Items { get; } = [];

    public bool HasLaunchers => Items.Count > 0;

    private bool _refreshing;

    /// <summary>Lanceurs installés et ouverts, lus hors du thread de l'interface.</summary>
    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var states = await Task.Run(() => control.Installed().Select(l => (Launcher: l, Running: control.IsRunning(l))).ToList());
            if (!states.Select(s => s.Launcher.Store).SequenceEqual(Items.Select(i => i.Store)))
            {
                Items.Clear();
                foreach (var (launcher, _) in states) Items.Add(new LauncherItemViewModel(launcher, this));
                OnPropertyChanged(nameof(HasLaunchers));
            }
            foreach (var (launcher, running) in states) Items.First(i => i.Store == launcher.Store).IsRunning = running;
        }
        finally
        {
            _refreshing = false;
        }
    }

    internal async Task OpenAsync(LauncherItemViewModel item)
    {
        try
        {
            control.Open(item.Launcher);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Error($"{item.Name} non ouvert", ex);
            dialogs.ShowError($"Impossible d'ouvrir {item.Name}", "Le lanceur n'a pas pu être démarré.", ex.Message);
            return;
        }
        await WatchStateAsync(item, expectRunning: true);
    }

    internal async Task CloseAsync(LauncherItemViewModel item)
    {
        if (sessions.Current is { } session)
        {
            dialogs.ShowInfo($"Partie en cours : {session.Profile.Name}", $"Fermez d'abord le jeu : {item.Name} peut en avoir besoin jusqu'à la fin de la partie.");
            return;
        }
        var message = item.Store == GameSource.Steam
            ? "Steam se ferme comme avec son menu « Quitter ». Un téléchargement en cours s'arrête et reprendra à sa prochaine ouverture."
            : $"{item.Name} n'a pas de commande pour quitter (fermer sa fenêtre le range dans la zone de notification) : ses programmes sont " +
              "arrêtés, comme avec « Fin de tâche ». Un téléchargement en cours s'arrête et reprendra à sa prochaine ouverture ; évitez " +
              "pendant l'installation ou la mise à jour d'un jeu.";
        if (!dialogs.Confirm($"Fermer {item.Name} ?", message, $"Fermer {item.Name}")) return;

        item.IsBusy = true;
        try
        {
            await control.CloseAsync(item.Launcher);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Error($"{item.Name} non fermé", ex);
            dialogs.ShowError($"{item.Name} n'a pas été fermé", ex.Message, ex.Message);
        }
        finally
        {
            item.IsBusy = false;
        }
        await RefreshAsync();
    }

    /// <summary>Après une action : l'état est relu chaque seconde jusqu'à ce qu'il change (20 s au plus).</summary>
    private async Task WatchStateAsync(LauncherItemViewModel item, bool expectRunning)
    {
        for (var i = 0; i < 20 && item.IsRunning != expectRunning; i++)
        {
            await Task.Delay(1000);
            item.IsRunning = await Task.Run(() => control.IsRunning(item.Launcher));
        }
    }
}

public sealed partial class LauncherItemViewModel(LauncherApp launcher, LaunchersViewModel owner) : ObservableObject
{
    public LauncherApp Launcher { get; } = launcher;

    public GameSource Store => Launcher.Store;

    public string Name => Launcher.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(AccessibleName), nameof(OpenLabel))]
    private bool _isRunning;

    /// <summary>Fermeture en cours (le bouton se grise).</summary>
    [ObservableProperty]
    private bool _isBusy;

    public string StatusText => IsRunning ? "ouvert" : "fermé";

    public string OpenLabel => IsRunning ? $"Afficher {Name}" : $"Ouvrir {Name}";

    /// <summary>Libellé du menu (une liaison StringFormat ne s'applique PAS à MenuItem.Header, de type object : il affichait « Steam »).</summary>
    public string CloseLabel => $"Fermer {Name}…";

    public string AccessibleName => $"{Name}, {StatusText}. Entrée : ouvrir ou fermer.";

    [RelayCommand]
    private Task Open() => owner.OpenAsync(this);

    [RelayCommand]
    private Task Close() => owner.CloseAsync(this);
}
