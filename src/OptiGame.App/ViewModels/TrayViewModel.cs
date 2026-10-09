using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Sessions;

namespace OptiGame.App.ViewModels;

/// <summary>Un jeu du sous-menu « Jeux récents » de l'icône de notification.</summary>
public sealed record RecentGameItem(Guid Id, string Name, System.Windows.Input.ICommand Play)
{
    public string Header => $"Jouer à {Name}";
}

public sealed partial class TrayViewModel(SessionViewModel session, CallViewModel call, GameSessionManager sessions, IDialogService dialogs, UnsavedChangesGuard unsaved,
    IServiceProvider services) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>Nombre de jeux récents proposés dans le menu.</summary>
    private const int RecentCount = 5;

    public SessionViewModel Session { get; } = session;

    /// <summary>Appel vocal en cours : micro et raccrocher depuis le menu (fenêtre fermée pendant les parties).</summary>
    public CallViewModel Call { get; } = call;

    /// <summary>Jeux joués le plus récemment, relus à chaque ouverture du menu (aucun suivi en tâche de fond).</summary>
    public ObservableCollection<RecentGameItem> RecentGames { get; } = [];

    public bool HasRecentGames => RecentGames.Count > 0;

    /// <summary>Menu de l'icône ouvert : liste des jeux récents relue (pendant une partie : aucun, on ne lance pas un 2e jeu).</summary>
    public void RefreshRecentGames()
    {
        RecentGames.Clear();
        if (sessions.Current is null)
        {
            foreach (var (id, name) in Library.RecentGames(RecentCount)) RecentGames.Add(new RecentGameItem(id, name, PlayRecentCommand));
        }
        OnPropertyChanged(nameof(HasRecentGames));
    }

    [RelayCommand]
    private Task PlayRecentAsync(RecentGameItem? game) => game is null ? Task.CompletedTask : Library.PlayAsync(game.Id);

    /// <summary>Résolue à l'usage : Paramètres dépend de ce menu, et Mes jeux de Paramètres (pas de dépendance circulaire).</summary>
    private LibraryViewModel Library => (LibraryViewModel)services.GetService(typeof(LibraryViewModel))!;

    [RelayCommand]
    private static void ShowMainWindow() => ((App)Application.Current).ShowMainWindow();

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (!unsaved.ConfirmDiscard()) return;
        if (sessions.Current is { } current)
        {
            if (!dialogs.Confirm("Quitter OptiGame pendant la partie ?",
                    $"{current.Profile.Name} est en cours. En quittant, OptiGame arrête l'optimisation et restaure maintenant les réglages d'origine.",
                    "Quitter et restaurer"))
            {
                return;
            }
            await Task.Run(sessions.EndNow);
        }
        Application.Current.Shutdown();
    }
}
