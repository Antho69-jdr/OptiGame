using System.Windows;
using OptiGame.Core.Abstractions;

namespace OptiGame.App.Dialogs;

public partial class ProcessPickerDialog : Window
{
    private readonly List<Item> _items;

    public ProcessPickerDialog(IReadOnlyList<RunningProgram> programs)
    {
        InitializeComponent();
        _items = programs.Select(p => new Item(p)).ToList();
        List.ItemsSource = _items;
    }

    public IReadOnlyList<string> SelectedExeNames => _items.Where(i => i.IsSelected).Select(i => i.ExeName).ToList();

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed class Item(RunningProgram program)
    {
        public string ExeName => program.ExeName;

        public string Path => program.Path;

        public string Suffix =>
            (program.Description is { } d ? $" — {d}" : "") + (program.InstanceCount > 1 ? $" ({program.InstanceCount} processus)" : "");

        public bool IsSelected { get; set; }
    }
}
