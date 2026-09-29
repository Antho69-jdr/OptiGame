using CommunityToolkit.Mvvm.ComponentModel;

namespace OptiGame.App.ViewModels;

public sealed partial class MainViewModel(
    DiagnosticViewModel diagnostic,
    ProfilesViewModel profiles,
    SessionViewModel session,
    SettingsViewModel settings) : ObservableObject
{
    public string Title => "OptiGame";

    public DiagnosticViewModel Diagnostic { get; } = diagnostic;

    public ProfilesViewModel Profiles { get; } = profiles;

    public SessionViewModel Session { get; } = session;

    public SettingsViewModel Settings { get; } = settings;
}
