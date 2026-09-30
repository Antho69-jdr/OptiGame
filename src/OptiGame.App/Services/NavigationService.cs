namespace OptiGame.App.Services;

/// <summary>Navigation entre pages sans dépendance circulaire (la fenêtre principale écoute les demandes).</summary>
public sealed class NavigationService
{
    public event Action<object>? NavigateRequested;

    public void Navigate(object page) => NavigateRequested?.Invoke(page);
}
