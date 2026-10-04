using System.Globalization;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>
/// Chemin d'une image en cache → image WPF, décodée à la largeur donnée en paramètre (ex. 400 pour une jaquette de la grille)
/// pour limiter la mémoire, et gardée par <see cref="ImageLoader"/> (pas de nouveau décodage à chaque affichage).
/// Paramètre « 396,gray » : même chose en niveaux de gris (jeux possédés mais non installés).
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var options = (parameter as string ?? "").Split(',', StringSplitOptions.TrimEntries);
        return ImageLoader.Load(value as string, int.TryParse(options[0], out var pixels) ? pixels : 0, options.Contains("gray"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
