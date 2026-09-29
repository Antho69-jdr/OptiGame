using System.Windows;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Sessions;

namespace OptiGame.App.ViewModels;

public sealed partial class TrayViewModel(SessionViewModel session, GameSessionManager sessions, IDialogService dialogs)
{
    public SessionViewModel Session { get; } = session;

    [RelayCommand]
    private static void ShowMainWindow() => ((App)Application.Current).ShowMainWindow();

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (sessions.Current is { } current)
        {
            if (!dialogs.Confirm($"Une session de jeu est en cours ({current.Profile.Name}).\n\n" +
                                 "Quitter OptiGame restaure maintenant les réglages d'origine. Continuer ?"))
            {
                return;
            }
            await Task.Run(sessions.EndNow);
        }
        Application.Current.Shutdown();
    }
}
