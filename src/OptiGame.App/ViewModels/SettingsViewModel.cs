using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core;
using OptiGame.Platform.Startup;

namespace OptiGame.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AutoStartService _autoStart;
    private readonly IDialogService _dialogs;
    private bool _updating;

    public SettingsViewModel(AutoStartService autoStart, IDialogService dialogs, AppPaths paths)
    {
        _autoStart = autoStart;
        _dialogs = dialogs;
        DataFolder = paths.Root;
        RefreshAutoStart();
    }

    public string DataFolder { get; }

    public string ExePath { get; } = Environment.ProcessPath ?? "";

    [ObservableProperty]
    private bool _autoStartEnabled;

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_updating) return;
        try
        {
            if (value) _autoStart.Enable(ExePath, App.MinimizedArgument);
            else _autoStart.Disable();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
        RefreshAutoStart();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
    }

    private void RefreshAutoStart()
    {
        _updating = true;
        try
        {
            AutoStartEnabled = _autoStart.IsEnabled();
        }
        finally
        {
            _updating = false;
        }
    }
}
