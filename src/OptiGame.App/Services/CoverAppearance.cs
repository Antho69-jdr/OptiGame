using System.Windows;
using OptiGame.Core.Settings;

namespace OptiGame.App.Services;

/// <summary>
/// Apparence des jaquettes réglée dans Paramètres › Mes jeux : arrondi (ressource « Radius.Cover ») et taille (« Size.CoverWidth » /
/// « Size.CoverHeight », et largeur de décodage d'ImageLoader), remplacées au niveau de l'application. Les jaquettes les lisent
/// en DynamicResource : changement visible tout de suite (Mes jeux redemande ses images à la nouvelle taille).
/// </summary>
public static class CoverAppearance
{
    public static void Attach(AppSettingsStore settings)
    {
        Apply(settings.Get());
        settings.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => Apply(settings.Get()));
    }

    private static void Apply(AppSettings settings)
    {
        if (Application.Current is not { } app) return;
        var radius = new CornerRadius(CoverStyle.Radius(settings.CoverCornerRadius));
        if (app.Resources["Radius.Cover"] is not CornerRadius current || current != radius) app.Resources["Radius.Cover"] = radius;

        var width = CoverStyle.Width(settings.CoverSize);
        Converters.ImageLoader.GridCoverWidth = width;
        if (app.Resources["Size.CoverWidth"] is not double currentWidth || currentWidth != width)
        {
            app.Resources["Size.CoverWidth"] = width;
            app.Resources["Size.CoverHeight"] = CoverStyle.Height(settings.CoverSize);
        }
    }
}
