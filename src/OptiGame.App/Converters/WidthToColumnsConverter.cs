using System.Globalization;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>
/// Nombre de colonnes d'une grille de cartes selon la largeur disponible (unités WPF). ConverterParameter = seuils croissants
/// séparés par des virgules : « 720 » = 1 colonne en dessous de 720, sinon 2 ; « 720,880 » = 1, 2 à partir de 720, 3 à partir
/// de 880. Les cartes passent l'une sous l'autre en fenêtre étroite.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var thresholds = (parameter as string ?? "720")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.MaxValue)
            .ToList();
        return value is double width ? 1 + thresholds.Count(t => width >= t) : 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
