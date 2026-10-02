using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Services;
using OptiGame.Core.Logging;
using OptiGame.Core.Measurement;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Platform.Measurement;

namespace OptiGame.App.ViewModels;

/// <summary>Captures PresentMon : lancement, liste, statistiques et comparaison avant/après.</summary>
public sealed partial class MeasuresViewModel : ObservableObject
{
    private readonly CaptureStore _store;
    private readonly PresentMonRunner _runner;
    private readonly AppSettingsStore _settings;
    private readonly ProfileStore _profiles;
    private readonly GameSessionManager _sessions;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;
    private readonly FileLog _log;
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cancellation;
    private DateTimeOffset _recordingStartsAt;
    private DateTimeOffset _recordingEndsAt;

    public MeasuresViewModel(CaptureStore store, PresentMonRunner runner, AppSettingsStore settings, ProfileStore profiles,
        GameSessionManager sessions, IDialogService dialogs, TimeProvider time, FileLog log)
    {
        _store = store;
        _runner = runner;
        _settings = settings;
        _profiles = profiles;
        _sessions = sessions;
        _dialogs = dialogs;
        _time = time;
        _log = log;
        _countdown.Tick += (_, _) => UpdateCountdown();

        // PresentMon : chemin configuré, sinon celui du dossier des outils d'OptiGame (mémorisé).
        PresentMonPath = settings.Get().PresentMonPath;
        if (PresentMonPath is null && runner.FindInToolsDirectory() is { } found)
        {
            SetPresentMonPath(found);
        }

        profiles.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshTargets);
        RefreshTargets();
        RefreshCaptures();
    }

    /// <summary>La liste a été rechargée : la vue doit refléter la sélection (Before/After).</summary>
    public event EventHandler? SelectionReset;

    public ObservableCollection<CaptureTarget> Targets { get; } = [];

    public ObservableCollection<CaptureItemViewModel> Captures { get; } = [];

    public IReadOnlyList<int> DurationOptions { get; } = [30, 60, 120, 300];

    public IReadOnlyList<int> DelayOptions { get; } = [0, 5, 10, 15];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPresentMon))]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private string? _presentMonPath;

    public bool HasPresentMon => PresentMonPath is not null && File.Exists(PresentMonPath);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private CaptureTarget? _selectedTarget;

    /// <summary>Nom d'exe saisi à la main (prioritaire sur la liste).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private string _customExeName = "";

    [ObservableProperty] private int _selectedDuration = 60;
    [ObservableProperty] private int _selectedDelay = 10;
    [ObservableProperty] private string _label = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCaptureCommand))]
    private bool _isCapturing;

    [ObservableProperty] private string _captureStatus = "";

    // Sélection (1 ou 2 captures) : statistiques, comparaison et graphe.
    [ObservableProperty] private CaptureItemViewModel? _before;
    [ObservableProperty] private CaptureItemViewModel? _after;
    [ObservableProperty] private IReadOnlyList<double>? _beforeFrames;
    [ObservableProperty] private IReadOnlyList<double>? _afterFrames;
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _comparison = [];
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _isComparison;

    private string? TargetExe =>
        !string.IsNullOrWhiteSpace(CustomExeName)
            ? (CustomExeName.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? CustomExeName.Trim() : CustomExeName.Trim() + ".exe")
            : SelectedTarget?.ExeName;

    private bool CanStart() => !IsCapturing && HasPresentMon && TargetExe is not null;

    [RelayCommand]
    private void ChoosePresentMon()
    {
        if (_dialogs.PickProgram("Choisir PresentMon (version console, ex. PresentMon-2.6.0-x64.exe)", PresentMonPath) is { } path)
        {
            SetPresentMonPath(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartCaptureAsync()
    {
        var exe = TargetExe!;
        var now = _time.GetLocalNow();
        var label = string.IsNullOrWhiteSpace(Label) ? $"{Path.GetFileNameWithoutExtension(exe)} — {now:dd/MM HH:mm}" : Label.Trim();
        var csvName = $"{now:yyyyMMdd-HHmmss}_{Path.GetFileNameWithoutExtension(exe)}.csv";
        var request = new CaptureRequest(PresentMonPath!, exe, SelectedDuration, SelectedDelay, Path.Combine(_store.Directory, csvName));

        IsCapturing = true;
        _cancellation = new CancellationTokenSource();
        _recordingStartsAt = now.AddSeconds(SelectedDelay);
        _recordingEndsAt = _recordingStartsAt.AddSeconds(SelectedDuration);
        UpdateCountdown();
        _countdown.Start();
        try
        {
            var output = await _runner.RunAsync(request, _cancellation.Token);
            var record = await Task.Run(() => BuildRecord(request, label, csvName, now, output));
            _store.Add(record);
            RefreshCaptures(select: record.Id);
            Label = "";
            CaptureStatus = $"Capture terminée : {record.Stats.AverageFps:0} FPS moyens, 1 % low {record.Stats.OnePercentLowFps:0} FPS.";
        }
        catch (OperationCanceledException)
        {
            File.Delete(request.OutputCsv); // capture partielle, sans intérêt
            CaptureStatus = "Capture annulée.";
        }
        catch (FormatException ex)
        {
            // Fichier produit mais non reconnu : on le GARDE pour pouvoir l'analyser.
            var kept = Path.ChangeExtension(request.OutputCsv, null) + "-non-reconnu.csv";
            File.Move(request.OutputCsv, kept, overwrite: true);
            CaptureStatus = "Capture non reconnue (fichier conservé).";
            _log.Error($"CSV PresentMon non reconnu, conservé dans {kept}", ex);
            _dialogs.ShowError($"{ex.Message}\n\nLe fichier a été conservé pour analyse :\n{kept}");
        }
        catch (Exception ex)
        {
            CaptureStatus = "Capture échouée.";
            _log.Error("Capture échouée", ex);
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            _countdown.Stop();
            _cancellation.Dispose();
            _cancellation = null;
            IsCapturing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsCapturing))]
    private void CancelCapture() => _cancellation?.Cancel();

    [RelayCommand]
    private void DeleteSelected()
    {
        var selected = new[] { Before, After }.OfType<CaptureItemViewModel>().ToList();
        if (selected.Count == 0) return;
        if (!_dialogs.Confirm($"Supprimer {(selected.Count == 1 ? "cette capture" : "ces 2 captures")} et leur fichier CSV ?")) return;
        foreach (var item in selected) _store.Remove(item.Record.Id);
        RefreshCaptures();
    }

    /// <summary>Présélectionne un jeu (bouton « Mesurer les FPS » de la page d'un jeu).</summary>
    public void SelectTarget(string exeName)
    {
        RefreshTargets();
        if (Targets.FirstOrDefault(t => t.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase)) is { } target)
        {
            SelectedTarget = target;
            CustomExeName = "";
        }
        else
        {
            CustomExeName = exeName;
        }
    }

    /// <summary>Appelé par la vue quand la sélection de la liste change.</summary>
    public void UpdateSelection(IReadOnlyList<CaptureItemViewModel> selected)
    {
        // Avant = la plus ancienne des deux.
        var ordered = selected.Take(2).OrderBy(c => c.Record.CapturedAt).ToList();
        Before = ordered.ElementAtOrDefault(0);
        After = ordered.ElementAtOrDefault(1);
        HasSelection = Before is not null;
        IsComparison = After is not null;
        Comparison = Before is not null && After is not null ? ComparisonRow.Build(Before.Record.Stats, After.Record.Stats) : [];
        BeforeFrames = LoadFrames(Before);
        AfterFrames = LoadFrames(After);
    }

    private CaptureRecord BuildRecord(CaptureRequest request, string label, string csvName, DateTimeOffset capturedAt, string presentMonOutput)
    {
        var session = _sessions.Current;
        var activeProfile = session is not null &&
                            Path.GetFileName(session.Profile.ExePath).Equals(request.ProcessName, StringComparison.OrdinalIgnoreCase)
            ? session.Profile.Name
            : null;
        var preset = _profiles.GetAll()
            .FirstOrDefault(p => Path.GetFileName(p.ExePath).Equals(request.ProcessName, StringComparison.OrdinalIgnoreCase))?.GraphicsPreset;
        return Platform.Measurement.CaptureReader.Build(request, label, csvName, capturedAt, presentMonOutput, activeProfile, preset: preset);
    }

    private IReadOnlyList<double>? LoadFrames(CaptureItemViewModel? item)
    {
        if (item is null) return null;
        try
        {
            return _store.LoadFrameTimes(item.Record);
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            _log.Warn($"CSV de la capture « {item.Record.Label} » illisible : {ex.Message}");
            return null;
        }
    }

    private void UpdateCountdown()
    {
        var now = _time.GetLocalNow();
        CaptureStatus = now < _recordingStartsAt
            ? $"Démarrage dans {(int)Math.Ceiling((_recordingStartsAt - now).TotalSeconds)} s : passez dans le jeu et jouez normalement."
            : $"Capture en cours : {Math.Max(0, (int)Math.Ceiling((_recordingEndsAt - now).TotalSeconds))} s restantes…";
    }

    private void SetPresentMonPath(string path)
    {
        PresentMonPath = path;
        _settings.Update(s => s.PresentMonPath = path);
    }

    private void RefreshTargets()
    {
        var previous = SelectedTarget?.ExeName;
        Targets.Clear();
        foreach (var profile in _profiles.GetAll().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Targets.Add(new CaptureTarget(profile.Name, Path.GetFileName(profile.ExePath)));
        }
        SelectedTarget = Targets.FirstOrDefault(t => t.ExeName == previous) ?? Targets.FirstOrDefault();
    }

    private void RefreshCaptures(Guid? select = null)
    {
        Captures.Clear();
        foreach (var record in _store.GetAll())
        {
            Captures.Add(new CaptureItemViewModel(record, _time, record.Id == select));
        }
        UpdateSelection(Captures.Where(c => c.IsInitiallySelected).ToList());
        SelectionReset?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record CaptureTarget(string Name, string ExeName)
{
    public override string ToString() => $"{Name} ({ExeName})";
}

public sealed class CaptureItemViewModel(CaptureRecord record, TimeProvider time, bool isInitiallySelected)
{
    public CaptureRecord Record { get; } = record;

    public bool IsInitiallySelected { get; } = isInitiallySelected;

    public string Label => Record.Label;

    public string Game => Record.ProcessName;

    public string Date => TimeZoneInfo.ConvertTime(Record.CapturedAt, time.LocalTimeZone).ToString("dd/MM/yyyy HH:mm");

    public string Profile => Record.ActiveProfile ?? "—";

    public string AverageFps => $"{Record.Stats.AverageFps:0.0}";

    public string OnePercentLow => $"{Record.Stats.OnePercentLowFps:0.0}";

    public string PointOnePercentLow => $"{Record.Stats.PointOnePercentLowFps:0.0}";

    public string P99 => $"{Record.Stats.P99FrameTimeMs:0.00} ms";

    public string Summary =>
        $"{Record.Stats.FrameCount} images sur {Record.Stats.DurationSeconds:0} s — médiane {Record.Stats.MedianFrameTimeMs:0.00} ms, " +
        $"pire image {Record.Stats.MaxFrameTimeMs:0.0} ms" +
        (Record.Stats.IsReliable ? "" : $" — moins de {FrameStats.MinimumFrames} images : 1 % low peu fiable");
}

/// <summary>Ligne du tableau de comparaison ; <see cref="IsBetter"/> tient compte du sens de la mesure.</summary>
public sealed record ComparisonRow(string Metric, string Before, string After, string Change, bool? IsBetter)
{
    public static IReadOnlyList<ComparisonRow> Build(FrameStats before, FrameStats after)
    {
        var c = new FrameStatsComparison(before, after);
        return
        [
            Row("FPS moyens", before.AverageFps, after.AverageFps, "0.0", c.AverageFpsChangePercent, higherIsBetter: true),
            Row("1 % low (FPS)", before.OnePercentLowFps, after.OnePercentLowFps, "0.0", c.OnePercentLowChangePercent, higherIsBetter: true),
            Row("0,1 % low (FPS)", before.PointOnePercentLowFps, after.PointOnePercentLowFps, "0.0", c.PointOnePercentLowChangePercent, higherIsBetter: true),
            Row("Frametime P99 (ms)", before.P99FrameTimeMs, after.P99FrameTimeMs, "0.00", c.P99FrameTimeChangePercent, higherIsBetter: false),
        ];
    }

    private static ComparisonRow Row(string metric, double before, double after, string format, double change, bool higherIsBetter) =>
        new(metric, before.ToString(format), after.ToString(format), $"{change:+0.0;-0.0;0.0} %",
            Math.Abs(change) < 1 ? null : (change > 0) == higherIsBetter);
}
