using System.Globalization;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>
/// Hauteur proportionnelle à une hauteur disponible (unités WPF), bornée. ConverterParameter = « fraction,minimum,maximum » :
/// « 0.55,360,560 » = 55 % de la hauteur, jamais moins de 360 ni plus de 560 (bannière de la fiche du jeu, qui reste à hauteur de
/// son contenu si celui-ci est plus grand).
/// </summary>
public sealed class HeightFractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "0.5,0,10000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .ToArray();
        if (parts.Length != 3) return 0.0;
        var (fraction, minimum, maximum) = (parts[0], parts[1], parts[2]);
        return value is double height && double.IsFinite(height) ? Math.Clamp(height * fraction, minimum, maximum) : minimum;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
