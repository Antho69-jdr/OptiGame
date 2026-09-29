using System.Windows.Controls;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Views;

public partial class MeasuresView : UserControl
{
    public MeasuresView()
    {
        InitializeComponent();
        // Sélection posée par le ViewModel (ex. la capture qui vient d'être enregistrée).
        CaptureList.Loaded += (_, _) => SyncSelectionFromViewModel();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MeasuresViewModel old) old.SelectionReset -= OnSelectionReset;
            if (e.NewValue is MeasuresViewModel vm) vm.SelectionReset += OnSelectionReset;
        };
    }

    private void OnSelectionReset(object? sender, EventArgs e) => SyncSelectionFromViewModel();

    private bool _syncing;

    /// <summary>La sélection multiple d'une ListView ne se lie pas en XAML : on la transmet au ViewModel.</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || DataContext is not MeasuresViewModel vm) return;
        vm.UpdateSelection(CaptureList.SelectedItems.OfType<CaptureItemViewModel>().ToList());
    }

    private void SyncSelectionFromViewModel()
    {
        if (DataContext is not MeasuresViewModel vm) return;
        _syncing = true;
        try
        {
            CaptureList.SelectedItems.Clear();
            foreach (var item in new[] { vm.Before, vm.After }.OfType<CaptureItemViewModel>())
            {
                CaptureList.SelectedItems.Add(item);
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
