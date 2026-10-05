using System.Windows;
using System.Windows.Controls;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>Choix des programmes ouverts à fermer pendant les parties : liste à cocher accessible au clavier.</summary>
public partial class ProcessPickerDialog : DialogWindow
{
    public ProcessPickerDialog(IReadOnlyList<RunningProgram> programs)
    {
        InitializeComponent();
        List.ItemsSource = programs.Select(p => new Item(p)).ToList();
        InitialFocus = List;
    }

    public IReadOnlyList<string> SelectedExeNames => List.SelectedItems.OfType<Item>().Select(i => i.ExeName).ToList();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = List.SelectedItems.Count;
        AddButton.Content = count == 0 ? "Ajouter" : $"Ajouter {FrenchText.Count(count, "programme", "programmes")}";
        AddButton.IsEnabled = count > 0;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed class Item(RunningProgram program)
    {
        public string ExeName => program.ExeName;

        public string Path => program.Path;

        public bool CanSelect => true;

        public string Suffix =>
            (program.Description is { } d ? $" — {d}" : "") +
            (program.InstanceCount > 1 ? $" ({FrenchText.Count(program.InstanceCount, "processus", "processus")})" : "");

        public string AccessibleName => ExeName + (program.Description is { } d ? $", {d}" : "");

        public override string ToString() => AccessibleName;
    }
}
