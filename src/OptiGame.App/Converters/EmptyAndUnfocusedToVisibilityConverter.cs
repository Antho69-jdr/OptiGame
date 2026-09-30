using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OptiGame.App.Converters;

/// <summary>Texte indicatif d'un champ : visible si le texte est vide et le champ n'a pas le focus.</summary>
public sealed class EmptyAndUnfocusedToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [string text, bool focused] && text.Length == 0 && !focused ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
