using System.Globalization;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>
/// Nombre de colonnes d'une grille de cartes selon la largeur disponible (unités WPF) : 1 en dessous du seuil
/// (ConverterParameter, 720 par défaut), sinon 2. Les cartes passent l'une sous l'autre en fenêtre étroite.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var threshold = parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 720;
        return value is double width && width < threshold ? 1 : 2;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
