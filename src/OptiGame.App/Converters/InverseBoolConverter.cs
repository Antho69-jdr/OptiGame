using System.Globalization;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>Vrai ↔ faux (ex. case grisée pendant une lecture : IsEnabled = !IsBusy).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
