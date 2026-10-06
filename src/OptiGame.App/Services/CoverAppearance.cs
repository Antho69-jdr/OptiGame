using System.Windows;
using OptiGame.Core.Settings;

namespace OptiGame.App.Services;

/// <summary>
/// Arrondi des jaquettes réglé dans Paramètres › Mes jeux : remplace la ressource « Radius.Cover » du thème au niveau de
/// l'application. Les jaquettes de Mes jeux et de la fiche la lisent en DynamicResource : changement visible tout de suite.
/// </summary>
public static class CoverAppearance
{
    public static void Attach(AppSettingsStore settings)
    {
        Apply(settings.Get().CoverCornerRadius);
        settings.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => Apply(settings.Get().CoverCornerRadius));
    }

    private static void Apply(int radius)
    {
        var value = new CornerRadius(CoverStyle.Radius(radius));
        if (Application.Current is not { } app || app.Resources["Radius.Cover"] is CornerRadius current && current == value) return;
        app.Resources["Radius.Cover"] = value;
    }
}
