using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>Visible seulement si toutes les valeurs liées sont vraies (MultiBinding), sinon replié.</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
