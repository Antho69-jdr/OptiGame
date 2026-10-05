using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OptiGame.App.Converters;

/// <summary>
/// Niveau « Good » / « Fair » / « Poor » (note du jeu, disque) → couleur de statut du thème. La couleur double toujours un
/// mot ou un chiffre affiché à côté : elle ne porte jamais le sens seule.
/// </summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "Fair" => "Brush.Warning",
            "Poor" => "Brush.Danger",
            _ => "Brush.Ok",
        };
        return Application.Current.TryFindResource(key) as Brush ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Vrai → « , modifications non enregistrées » (ajouté au nom accessible d'un onglet modifié), faux → rien.</summary>
public sealed class DirtySuffixConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ", modifications non enregistrées" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
