using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace OptiGame.App.ViewModels;

public sealed partial class TrayViewModel
{
    [RelayCommand]
    private static void ShowMainWindow() => ((App)Application.Current).ShowMainWindow();

    [RelayCommand]
    private static void Exit() => Application.Current.Shutdown();
}
