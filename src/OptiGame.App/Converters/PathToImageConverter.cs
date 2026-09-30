using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace OptiGame.App.Converters;

/// <summary>
/// Chemin d'une image en cache → image WPF. Chargée entièrement en mémoire (le fichier n'est pas verrouillé) et
/// décodée à la largeur donnée en paramètre (ex. 400 pour une jaquette de la grille) pour limiter la mémoire.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (parameter is string width && int.TryParse(width, out var pixels)) image.DecodePixelWidth = pixels;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null; // Image corrompue : la jaquette de remplacement s'affiche.
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
