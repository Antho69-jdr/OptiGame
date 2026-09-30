using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Abstractions;
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
