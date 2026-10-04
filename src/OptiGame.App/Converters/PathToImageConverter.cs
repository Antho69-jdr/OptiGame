using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OptiGame.App.Converters;

/// <summary>
/// Chemin d'une image en cache → image WPF. Chargée entièrement en mémoire (le fichier n'est pas verrouillé) et
/// décodée à la largeur donnée en paramètre (ex. 400 pour une jaquette de la grille) pour limiter la mémoire.
/// Paramètre « 396,gray » : même chose en niveaux de gris (jeux possédés mais non installés).
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        var options = (parameter as string ?? "").Split(',', StringSplitOptions.TrimEntries);
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (int.TryParse(options[0], out var pixels)) image.DecodePixelWidth = pixels;
            image.EndInit();
            image.Freeze();
            if (!options.Contains("gray")) return image;

            var gray = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
            gray.Freeze();
            return gray;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null; // Image corrompue : la jaquette de remplacement s'affiche.
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
