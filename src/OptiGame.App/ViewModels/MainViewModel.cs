using CommunityToolkit.Mvvm.ComponentModel;

namespace OptiGame.App.ViewModels;

public sealed partial class MainViewModel(DiagnosticViewModel diagnostic, ProfilesViewModel profiles) : ObservableObject
{
    public string Title => "OptiGame";

    public DiagnosticViewModel Diagnostic { get; } = diagnostic;

    public ProfilesViewModel Profiles { get; } = profiles;
}
