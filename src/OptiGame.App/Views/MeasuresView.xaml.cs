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
        // Abonné seulement tant que la vue est affichée : le ViewModel (unique) ne doit pas garder en vie une vue retirée, ni
        // la fenêtre fermée pendant une partie (une vue garde son parent, donc toute la fenêtre).
        Loaded += (_, _) => Attach(DataContext as MeasuresViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    private MeasuresViewModel? _attached;

    private void Attach(MeasuresViewModel? vm)
    {
        if (_attached is not null) _attached.SelectionReset -= OnSelectionReset;
        _attached = vm;
        if (_attached is not null) _attached.SelectionReset += OnSelectionReset;
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
