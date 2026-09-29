using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Library;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;

namespace OptiGame.App.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly IPowerSchemeProvider _power;
    private readonly IRunningProgramsProvider _programs;
    private readonly IDialogService _dialogs;
    private readonly IGameLibraryScanner _scanner;
    private readonly AppSettingsStore _settings;
    private IReadOnlyList<PowerScheme>? _schemes;

    public ProfilesViewModel(ProfileStore store, IPowerSchemeProvider power, IRunningProgramsProvider programs,
        IDialogService dialogs, IGameLibraryScanner scanner, AppSettingsStore settings)
    {
        _store = store;
        _power = power;
        _programs = programs;
        _dialogs = dialogs;
        _scanner = scanner;
        _settings = settings;
        Reload(selectId: null);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanGamesCommand))]
    private bool _isScanning;

    private bool CanScan() => CanChangeSelection && !IsScanning;

    /// <summary>Recherche des jeux installés (Steam + dossiers de jeux), lecture seule.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanGamesAsync()
    {
        var folders = _settings.Get().GameFolders;
        IReadOnlyList<InstalledGame> games;
        IsScanning = true;
        try
        {
            games = await Task.Run(() => _scanner.Scan(folders));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("La recherche des jeux a échoué.\n\n" + ex.Message);
            return;
        }
        finally
        {
            IsScanning = false;
        }

        var existing = _store.GetAll();
        var selection = _dialogs.PickInstalledGames(games, path => existing.Any(p => p.Matches(path)), folders);
        var errors = new List<string>();
        Guid? last = null;
        foreach (var (game, exe) in selection)
        {
            var profile = new GameProfile { Name = CleanName(game.Name), ExePath = exe.Path };
            try
            {
                _store.Save(profile);
                last = profile.Id;
            }
            catch (ProfileValidationException ex)
            {
                errors.Add($"{game.Name} : {ex.Message}");
            }
        }

        if (last is not null) Reload(last);
        if (errors.Count > 0) _dialogs.ShowError("Certains profils n'ont pas été créés :\n\n" + string.Join("\n", errors));
    }

    private static string CleanName(string name) => name.Replace("®", "").Replace("™", "").Trim();

    public ObservableCollection<ProfileListItem> Profiles { get; } = [];

    [ObservableProperty]
    private ProfileListItem? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeSelection))]
    [NotifyCanExecuteChangedFor(nameof(AddGameCommand))]
    private ProfileEditorViewModel? _editor;

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>La liste est verrouillée tant que le profil affiché a des modifications non enregistrées.</summary>
    public bool CanChangeSelection => Editor is not { IsDirty: true };

    private IReadOnlyList<PowerScheme> Schemes
    {
        get
        {
            try
            {
                return _schemes ??= _power.GetSchemes();
            }
            catch (Exception)
            {
                return _schemes = [];
            }
        }
    }

    partial void OnSelectedProfileChanged(ProfileListItem? value) => OpenEditor(value?.Id);

    partial void OnEditorChanged(ProfileEditorViewModel? oldValue, ProfileEditorViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnEditorPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnEditorPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileEditorViewModel.IsDirty))
        {
            OnPropertyChanged(nameof(CanChangeSelection));
            AddGameCommand.NotifyCanExecuteChanged();
            ScanGamesCommand.NotifyCanExecuteChanged();
        }
    }

    private void OpenEditor(Guid? id) =>
        Editor = id is { } profileId && _store.Find(profileId) is { } profile
            ? new ProfileEditorViewModel(profile, Schemes, _programs, _dialogs, SaveAsync, Delete, () => OpenEditor(profileId))
            : null;

    [RelayCommand(CanExecute = nameof(CanChangeSelection))]
    private void AddGame()
    {
        var exe = _dialogs.PickExecutable(null);
        if (exe is null) return;

        if (_store.GetAll().FirstOrDefault(p => p.Matches(exe)) is { } existing)
        {
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == existing.Id);
            return;
        }

        var profile = new GameProfile { Name = DialogService.FriendlyName(exe), ExePath = exe };
        try
        {
            _store.Save(profile);
        }
        catch (ProfileValidationException ex)
        {
            _dialogs.ShowError(ex.Message);
            return;
        }
        Reload(profile.Id);
    }

    private Task SaveAsync(GameProfile profile)
    {
        try
        {
            _store.Save(profile);
        }
        catch (ProfileValidationException ex)
        {
            _dialogs.ShowError("Le profil n'a pas été enregistré :\n\n" + ex.Message);
            return Task.CompletedTask;
        }
        Reload(profile.Id);
        return Task.CompletedTask;
    }

    private void Delete(GameProfile profile)
    {
        if (!_dialogs.Confirm($"Supprimer le profil « {profile.Name} » ?\n\nAucun réglage n'est modifié par cette suppression.")) return;
        _store.Remove(profile.Id);
        Reload(selectId: null);
    }

    private void Reload(Guid? selectId)
    {
        // L'éditeur est reconstruit à partir de la version enregistrée.
        Editor = null;
        SelectedProfile = null;
        Profiles.Clear();
        foreach (var profile in _store.GetAll().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Profiles.Add(new ProfileListItem(profile));
        }
        OnPropertyChanged(nameof(HasProfiles));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectId) ?? Profiles.FirstOrDefault();
    }
}

public sealed class ProfileListItem(GameProfile profile)
{
    public Guid Id => profile.Id;

    public string Name => profile.Name;

    public string Detail => Path.GetFileName(profile.ExePath) + (profile.Enabled ? "" : " — désactivé");
}

public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private readonly GameProfile _original;
    private readonly IRunningProgramsProvider _programs;
    private readonly IDialogService _dialogs;
    private readonly Func<GameProfile, Task> _save;
    private readonly Action<GameProfile> _delete;
    private readonly Action _revert;
    private readonly IReadOnlyList<PowerScheme> _schemes;

    public ProfileEditorViewModel(GameProfile profile, IReadOnlyList<PowerScheme> schemes, IRunningProgramsProvider programs,
        IDialogService dialogs, Func<GameProfile, Task> save, Action<GameProfile> delete, Action revert)
    {
        _original = profile;
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
        foreach (var process in profile.ProcessesToClose)
        {
            AddProcessRow(process.ExeName, process.Relaunch);
        }
        IsDirty = false;
        UpdateSummary();
    }

    public List<PowerSchemeOption> PowerSchemeOptions { get; }

    public IReadOnlyList<PriorityOption> PriorityOptions { get; } =
    [
        new(GamePriority.Normal, "Normale (inchangée)"),
        new(GamePriority.AboveNormal, "Supérieure à la normale"),
        new(GamePriority.High, "Haute"),
    ];

    public ObservableCollection<ProcessRowViewModel> ProcessesToClose { get; } = [];

    public ObservableCollection<string> Summary { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _exePath;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private PowerSchemeOption _selectedPowerScheme;
    [ObservableProperty] private PriorityOption _selectedPriority;
    [ObservableProperty] private string _newProcessName = "";
    [ObservableProperty] private bool _isDirty;

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
            _dialogs.ShowError("Impossible de lister les programmes ouverts.\n\n" + ex.Message);
            return;
        }

        foreach (var exe in _dialogs.PickRunningPrograms(running))
        {
            AddIfMissing(exe);
        }
    }

    [RelayCommand]
    private async Task SaveAsync() => await _save(ToProfile());

    [RelayCommand]
    private void Delete() => _delete(_original);

    /// <summary>Recharge la version enregistrée.</summary>
    [RelayCommand]
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
        IsDirty = true;
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
            : "Profil désactivé : rien ne sera appliqué. S'il était activé, au démarrage du jeu :");
        foreach (var line in Core.Sessions.SessionPlan.Describe(profile, _schemes))
        {
            Summary.Add("• " + line);
        }
        Summary.Add("Quand le jeu se ferme, tout ce qui a été modifié est restauré.");
    }
}

public sealed record PowerSchemeOption(Guid? Id, string Label);

public sealed record PriorityOption(GamePriority Value, string Label);

public sealed partial class ProcessRowViewModel(string exeName, bool relaunch, Action<ProcessRowViewModel> remove) : ObservableObject
{
    public string ExeName { get; } = exeName;

    [ObservableProperty]
    private bool _relaunch = relaunch;

    [RelayCommand]
    private void Remove() => remove(this);
}
