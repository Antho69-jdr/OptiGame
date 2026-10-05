using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Launching;
using OptiGame.Core.Profiles;

namespace OptiGame.App.ViewModels;

public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private readonly GameProfile _original;
    private readonly IRunningProgramsProvider _programs;
    private readonly IDialogService _dialogs;
    private readonly Func<GameProfile, Task> _save;
    private readonly Action<GameProfile> _delete;
    private readonly Action _revert;
    private readonly IReadOnlyList<PowerScheme> _schemes;
    private readonly Func<GameProfile, string> _describeLaunch;

    public ProfileEditorViewModel(GameProfile profile, IReadOnlyList<PowerScheme> schemes, IRunningProgramsProvider programs,
        IDialogService dialogs, Func<GameProfile, Task> save, Action<GameProfile> delete, Action revert,
        Func<GameProfile, string> describeLaunch)
    {
        _describeLaunch = describeLaunch;
        _original = profile;
        ProcessesToClose.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProcessesToClose));
        _schemes = schemes;
        _programs = programs;
        _dialogs = dialogs;
        _save = save;
        _delete = delete;
        _revert = revert;

        PowerSchemeOptions = [new PowerSchemeOption(null, "Ne pas changer"), .. schemes.Select(s => new PowerSchemeOption(s.Id, s.Name))];
        if (profile.PowerSchemeId is { } id && PowerSchemeOptions.All(o => o.Id != id))
        {
            PowerSchemeOptions.Add(new PowerSchemeOption(id, $"Plan introuvable ({id})"));
        }

        _name = profile.Name;
        _exePath = profile.ExePath;
        _enabled = profile.Enabled;
        _selectedPowerScheme = PowerSchemeOptions.First(o => o.Id == profile.PowerSchemeId);
        _selectedPriority = PriorityOptions.First(o => o.Value == profile.Priority);
        _selectedLaunchMode = LaunchModeOptions.First(o => o.Value == profile.LaunchMode);
        _steamAppId = profile.SteamAppId ?? "";
        _launcherPath = profile.LauncherPath ?? "";
        _launchArguments = profile.LaunchArguments ?? "";
        foreach (var process in profile.ProcessesToClose)
        {
            AddProcessRow(process.ExeName, process.Relaunch);
        }
        IsDirty = false;
        UpdateSummary();
        UpdateLaunchPreview();
    }

    public IReadOnlyList<LaunchModeOption> LaunchModeOptions { get; } =
    [
        new(LaunchMode.Automatic, "Automatique (recommandé) : Steam si possible, sinon le fichier .exe"),
        new(LaunchMode.Steam, "Par Steam"),
        new(LaunchMode.Executable, "Fichier .exe du jeu"),
        new(LaunchMode.Launcher, "Autre lanceur (ex. RSI Launcher)"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSteamMode), nameof(IsLauncherMode))]
    private LaunchModeOption _selectedLaunchMode;

    public bool IsSteamMode => SelectedLaunchMode?.Value is LaunchMode.Steam or LaunchMode.Automatic;

    public bool IsLauncherMode => SelectedLaunchMode?.Value == LaunchMode.Launcher;

    [ObservableProperty] private string _steamAppId;
    [ObservableProperty] private string _launcherPath;
    [ObservableProperty] private string _launchArguments;

    /// <summary>Ce que « Jouer » lancera, avec les réglages en cours d'édition.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchSummary))]
    private string _launchPreview = "";

    /// <summary>Première ligne de l'aperçu (ex. « Steam (appid 2357570) — automatique »), pour la liste de la fiche.</summary>
    public string LaunchSummary => LaunchPreview.Split('\n')[0].Trim();

    partial void OnSelectedLaunchModeChanged(LaunchModeOption value) => TouchLaunch();
    partial void OnSteamAppIdChanged(string value) => TouchLaunch();
    partial void OnLauncherPathChanged(string value) => TouchLaunch();
    partial void OnLaunchArgumentsChanged(string value) => TouchLaunch();

    [RelayCommand]
    private void BrowseLauncher()
    {
        if (_dialogs.PickProgram("Choisir le lanceur du jeu (ex. RSI Launcher.exe)", LauncherPath.Length > 0 ? LauncherPath : ExePath) is { } exe)
        {
            LauncherPath = exe;
        }
    }

    private void TouchLaunch()
    {
        Touch();
        UpdateLaunchPreview();
    }

    private void UpdateLaunchPreview()
    {
        if (SelectedLaunchMode is null) return;
        LaunchPreview = _describeLaunch(ToProfile());
    }

    public List<PowerSchemeOption> PowerSchemeOptions { get; }

    public IReadOnlyList<PriorityOption> PriorityOptions { get; } =
    [
        new(GamePriority.Normal, "Normale (inchangée)"),
        new(GamePriority.AboveNormal, "Supérieure à la normale"),
        new(GamePriority.High, "Haute"),
    ];

    public ObservableCollection<ProcessRowViewModel> ProcessesToClose { get; } = [];

    public bool HasProcessesToClose => ProcessesToClose.Count > 0;

    public ObservableCollection<string> Summary { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _exePath;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private PowerSchemeOption _selectedPowerScheme;
    [ObservableProperty] private PriorityOption _selectedPriority;
    [ObservableProperty] private string _newProcessName = "";

    /// <summary>Différent de la version enregistrée (comparaison, pas un simple drapeau : revenir à l'original efface la barre).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    private bool _isDirty;

    /// <summary>Modifications dans l'onglet « Optimisation » (réglages de partie).</summary>
    [ObservableProperty] private bool _isOptimizationDirty;

    /// <summary>Modifications dans l'onglet « Propriétés » (nom, fichier .exe, lancement).</summary>
    [ObservableProperty] private bool _isPropertiesDirty;

    /// <summary>Erreur de saisie affichée sous le champ du nom ; vide si tout va bien.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _nameError = "";

    partial void OnNameChanged(string value) => Touch();
    partial void OnExePathChanged(string value) => Touch();
    partial void OnEnabledChanged(bool value) => Touch();
    partial void OnSelectedPowerSchemeChanged(PowerSchemeOption value) => Touch();
    partial void OnSelectedPriorityChanged(PriorityOption value) => Touch();

    [RelayCommand]
    private void ChangeExe()
    {
        if (_dialogs.PickExecutable(ExePath) is { } exe) ExePath = exe;
    }

    [RelayCommand]
    private void AddProcess()
    {
        var name = NewProcessName.Trim();
        if (name.Length == 0) return;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        AddIfMissing(name);
        NewProcessName = "";
    }

    [RelayCommand]
    private void PickRunningPrograms()
    {
        IReadOnlyList<RunningProgram> running;
        try
        {
            running = _programs.GetUserPrograms()
                .Where(p => !p.ExeName.Equals(Path.GetFileName(ExePath), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Programmes ouverts illisibles", "OptiGame n'a pas pu lister les programmes ouverts. Saisissez le nom du programme (ex. chrome.exe).", ex.Message);
            return;
        }
        if (running.Count == 0)
        {
            _dialogs.ShowInfo("Aucun programme à proposer",
                "Aucun programme ouvert ne peut être fermé pendant les parties (les composants de Windows et les services ne sont jamais proposés).");
            return;
        }

        foreach (var exe in _dialogs.PickRunningPrograms(running))
        {
            AddIfMissing(exe);
        }
    }

    private bool CanSave() => IsDirty && NameError.Length == 0;

    /// <summary>« Enregistrer » (ou Ctrl+S) : seulement s'il y a des modifications valides.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync() => await _save(ToProfile());

    [RelayCommand]
    private void Delete() => _delete(_original);

    /// <summary>« Abandonner les modifications » : recharge la version enregistrée.</summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert() => _revert();

    private GameProfile ToProfile() => new()
    {
        Id = _original.Id,
        Name = Name.Trim(),
        ExePath = ExePath,
        Enabled = Enabled,
        PowerSchemeId = SelectedPowerScheme.Id,
        Priority = SelectedPriority.Value,
        ProcessesToClose = ProcessesToClose.Select(p => new ProcessToClose { ExeName = p.ExeName, Relaunch = p.Relaunch }).ToList(),
        LaunchMode = SelectedLaunchMode?.Value ?? LaunchMode.Automatic,
        SteamAppId = string.IsNullOrWhiteSpace(SteamAppId) ? null : SteamAppId.Trim(),
        LauncherPath = string.IsNullOrWhiteSpace(LauncherPath) ? null : LauncherPath.Trim(),
        LaunchArguments = string.IsNullOrWhiteSpace(LaunchArguments) ? null : LaunchArguments.Trim(),
    };

    private void AddIfMissing(string exeName)
    {
        if (ProcessesToClose.Any(p => p.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase))) return;
        AddProcessRow(exeName, relaunch: true);
        Touch();
    }

    private void AddProcessRow(string exeName, bool relaunch)
    {
        var row = new ProcessRowViewModel(exeName, relaunch, r =>
        {
            ProcessesToClose.Remove(r);
            Touch();
        });
        row.PropertyChanged += (_, _) => Touch();
        ProcessesToClose.Add(row);
    }

    private void Touch()
    {
        if (SelectedPowerScheme is null || SelectedPriority is null || SelectedLaunchMode is null) return; // construction
        var current = ToProfile();
        IsOptimizationDirty = current.Enabled != _original.Enabled || current.PowerSchemeId != _original.PowerSchemeId ||
                              current.Priority != _original.Priority ||
                              !current.ProcessesToClose.Select(p => (p.ExeName.ToLowerInvariant(), p.Relaunch))
                                  .SequenceEqual(_original.ProcessesToClose.Select(p => (p.ExeName.ToLowerInvariant(), p.Relaunch)));
        IsPropertiesDirty = current.Name != _original.Name.Trim() || !string.Equals(current.ExePath, _original.ExePath, StringComparison.OrdinalIgnoreCase) ||
                            current.LaunchMode != _original.LaunchMode || current.SteamAppId != _original.SteamAppId ||
                            current.LauncherPath != _original.LauncherPath || current.LaunchArguments != _original.LaunchArguments;
        IsDirty = IsOptimizationDirty || IsPropertiesDirty;
        NameError = string.IsNullOrWhiteSpace(Name) ? "Donnez un nom au jeu." : "";
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        Summary.Clear();
        // SelectedPowerScheme/SelectedPriority peuvent être null pendant la construction.
        if (SelectedPowerScheme is null || SelectedPriority is null) return;
        var profile = ToProfile();
        Summary.Add(profile.Enabled
            ? $"Quand {Path.GetFileName(profile.ExePath)} démarre :"
            : "Optimisation désactivée : rien ne sera appliqué. Si elle était activée, au démarrage du jeu :");
        foreach (var line in Core.Sessions.SessionPlan.Describe(profile, _schemes))
        {
            Summary.Add("• " + line);
        }
        Summary.Add("Quand le jeu se ferme, tout ce qui a été modifié est restauré.");
    }
}

public sealed record PowerSchemeOption(Guid? Id, string Label);

public sealed record PriorityOption(GamePriority Value, string Label);

public sealed record LaunchModeOption(LaunchMode Value, string Label);

public sealed partial class ProcessRowViewModel(string exeName, bool relaunch, Action<ProcessRowViewModel> remove) : ObservableObject
{
    public string ExeName { get; } = exeName;

    [ObservableProperty]
    private bool _relaunch = relaunch;

    [RelayCommand]
    private void Remove() => remove(this);
}
