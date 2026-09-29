using CommunityToolkit.Mvvm.ComponentModel;

namespace OptiGame.App.ViewModels;

public sealed partial class MainViewModel(DiagnosticViewModel diagnostic) : ObservableObject
{
    public string Title => "OptiGame";

    public DiagnosticViewModel Diagnostic { get; } = diagnostic;
}
