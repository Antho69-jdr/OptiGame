using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.Services;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.State;
using OptiGame.Platform;

namespace OptiGame.App.ViewModels;

public sealed partial class DiagnosticViewModel(
    DiagnosticRunner runner,
    [FromKeyedServices(JournalKeys.Fixes)] ChangeJournal fixes,
    IDialogService dialogs,
    TimeProvider time,
    NavigationService navigation,
    DriversViewModel drivers) : ObservableObject
{
    /// <summary>Lien d'un contrôle vers une autre page (ex. « Rechercher les mises à jour de pilotes »).</summary>
    [RelayCommand]
    private void OpenLink(DiagnosticLinkTarget target)
    {
        switch (target)
        {
            case DiagnosticLinkTarget.Drivers:
                navigation.Navigate(drivers);
                break;
        }
    }

    public ObservableCollection<DiagnosticItemViewModel> Items { get; } = [];

    /// <summary>Corrections appliquées par OptiGame et encore actives (annulables).</summary>
    public ObservableCollection<AppliedFixViewModel> AppliedFixes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Le diagnostic lit la configuration sans rien modifier.";

    [ObservableProperty]
    private bool _hasAppliedFixes;

    private bool CanRun() => !IsBusy;

    /// <summary>Lecture seule : aucun réglage n'est modifié.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsBusy = true;
        StatusText = "Analyse en cours…";
        try
        {
            // WMI et P/Invoke hors du thread UI.
            var results = await Task.Run(runner.RunAll);
            Items.Clear();
            foreach (var result in results)
            {
                var fixVms = result.Fixes
                    .Where(f => !fixes.IsActive(f.Change.Id))
                    .Select(f => new FixViewModel(f, ApplyFixAsync))
                    .ToList();
                Items.Add(new DiagnosticItemViewModel(result, fixVms));
            }

            RefreshAppliedFixes();
            var attention = results.Count(r => r.Status == DiagnosticStatus.NeedsAttention);
            StatusText = $"Dernière analyse : {time.GetLocalNow():HH:mm:ss} — " +
                (attention == 0 ? "rien à corriger." : $"{attention} point(s) à corriger.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyFixAsync(DiagnosticFix fix)
    {
        if (IsBusy || !dialogs.ConfirmChange(fix.Change, fix.IsAdvanced))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => fixes.Apply(fix.Change));
        }
        catch (Exception ex)
        {
            dialogs.ShowError($"La correction « {fix.Change.Title} » n'a pas pu être appliquée ; rien n'a été modifié.\n\n{ex.Message}");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (fix.Change.RequiresReboot)
        {
            dialogs.ShowInfo($"« {fix.Change.Title} » est enregistré. Redémarrez Windows pour qu'il soit pris en compte.");
        }
        await RunAsync();
    }

    private async Task UndoAsync(ChangeRecord change)
    {
        if (IsBusy || !dialogs.ConfirmUndo(change))
        {
            return;
        }

        IsBusy = true;
        RestoreReport report;
        try
        {
            report = await Task.Run(() => fixes.Undo(change.Id));
        }
        finally
        {
            IsBusy = false;
        }

        if (!report.Success)
        {
            dialogs.ShowError("La restauration a échoué pour :\n" +
                string.Join("\n", report.Failed.Select(f => $"• {f.Target} : {f.Error}")) +
                "\n\nLa correction reste listée ; vous pourrez réessayer.");
        }
        else if (report.ModifiedExternally.Count > 0)
        {
            dialogs.ShowInfo("Réglage restauré. Remarque : il avait été modifié entre-temps par autre chose qu'OptiGame.");
        }
        else if (change.RequiresReboot)
        {
            dialogs.ShowInfo("Réglage d'origine restauré. Redémarrez Windows pour qu'il soit pris en compte.");
        }

        await RunAsync();
    }

    private void RefreshAppliedFixes()
    {
        AppliedFixes.Clear();
        foreach (var change in fixes.ActiveChanges)
        {
            AppliedFixes.Add(new AppliedFixViewModel(change, time, UndoAsync));
        }
        HasAppliedFixes = AppliedFixes.Count > 0;
    }
}

public sealed class DiagnosticItemViewModel(DiagnosticResult result, IReadOnlyList<FixViewModel> fixes)
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

    public IReadOnlyList<FixViewModel> Fixes { get; } = fixes.Where(f => !f.IsAdvanced).ToList();

    public IReadOnlyList<FixViewModel> AdvancedFixes { get; } = fixes.Where(f => f.IsAdvanced).ToList();

    public bool HasAdvancedFixes => AdvancedFixes.Count > 0;

    public bool HasLink => Result.Link is not null;

    public string LinkLabel => Result.Link?.Label ?? "";

    public DiagnosticLinkTarget LinkTarget => Result.Link?.Target ?? default;
}

public sealed partial class FixViewModel(DiagnosticFix fix, Func<DiagnosticFix, Task> apply)
{
    public string Title => fix.Change.Title;

    public bool IsAdvanced => fix.IsAdvanced;

    public string Badges => string.Join(" · ", new[]
    {
        fix.Change.RequiresAdmin ? "admin" : null,
        fix.Change.RequiresReboot ? "redémarrage" : null,
    }.OfType<string>());

    [RelayCommand]
    private Task ApplyAsync() => apply(fix);
}

public sealed partial class AppliedFixViewModel(ChangeRecord change, TimeProvider time, Func<ChangeRecord, Task> undo)
{
    public string Title => change.Title;

    public string What => change.What;

    public string AppliedAt =>
        $"Appliquée le {TimeZoneInfo.ConvertTime(change.AppliedAt, time.LocalTimeZone):dd/MM/yyyy à HH:mm}";

    [RelayCommand]
    private Task UndoAsync() => undo(change);
}
