using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.Core.Diagnostics;

namespace OptiGame.App.ViewModels;

public sealed partial class DiagnosticViewModel(DiagnosticRunner runner, TimeProvider time) : ObservableObject
{
    public ObservableCollection<DiagnosticItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Le diagnostic lit la configuration sans rien modifier.";

    private bool CanRun() => !IsRunning;

    /// <summary>Lecture seule : aucun réglage n'est modifié.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        StatusText = "Analyse en cours…";
        try
        {
            // WMI et P/Invoke hors du thread UI.
            var results = await Task.Run(runner.RunAll);
            Items.Clear();
            foreach (var result in results)
            {
                Items.Add(new DiagnosticItemViewModel(result));
            }

            var attention = results.Count(r => r.Status == DiagnosticStatus.NeedsAttention);
            StatusText = $"Dernière analyse : {time.GetLocalNow():HH:mm:ss} — " +
                (attention == 0 ? "rien à corriger." : $"{attention} point(s) à corriger.");
        }
        finally
        {
            IsRunning = false;
        }
    }
}

public sealed class DiagnosticItemViewModel(DiagnosticResult result)
{
    public DiagnosticResult Result { get; } = result;

    public string Title => Result.IsEstimate ? $"{Result.Title} (estimation)" : Result.Title;

    public DiagnosticStatus Status => Result.Status;

    public string StatusLabel => Result.Status switch
    {
        DiagnosticStatus.Ok => "OK",
        DiagnosticStatus.NeedsAttention => "À corriger",
        DiagnosticStatus.Info => "Info",
        _ => "Erreur",
    };

    public string Summary => Result.Summary;

    public string Explanation => Result.Explanation;

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Result.Explanation);

    public IReadOnlyList<string> Details => Result.Details;
}
