using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>Visible quand la valeur est fausse (ex. bandeau « PresentMon introuvable »).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
