using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core;
using OptiGame.Core.Settings;
using OptiGame.Platform.Startup;

namespace OptiGame.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AutoStartService _autoStart;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsStore _settings;
    private bool _updating;

    public SettingsViewModel(AutoStartService autoStart, IDialogService dialogs, AppPaths paths, AppSettingsStore settings)
    {
        _autoStart = autoStart;
        _dialogs = dialogs;
        _settings = settings;
        DataFolder = paths.Root;
        RefreshAutoStart();
        RefreshGameFolders();
    }

    public string DataFolder { get; }

    public string ExePath { get; } = Environment.ProcessPath ?? "";

    /// <summary>Dossiers dont chaque sous-dossier est un jeu, pour la recherche des jeux installés.</summary>
    public ObservableCollection<string> GameFolders { get; } = [];

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
    private void AddGameFolder()
    {
        if (_dialogs.PickFolder("Choisir un dossier contenant des jeux (un sous-dossier par jeu)") is not { } folder) return;
        _settings.Update(s =>
        {
            if (!s.GameFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) s.GameFolders.Add(folder);
        });
        RefreshGameFolders();
    }

    [RelayCommand]
    private void RemoveGameFolder(string folder)
    {
        _settings.Update(s => s.GameFolders.RemoveAll(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase)));
        RefreshGameFolders();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
    }

    private void RefreshGameFolders()
    {
        GameFolders.Clear();
        foreach (var folder in _settings.Get().GameFolders) GameFolders.Add(folder);
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
