using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OptiGame.Core.Diagnostics;

namespace OptiGame.App.Converters;

/// <summary>
/// Couleur d'un statut de diagnostic, prise dans le thème. Avec ConverterParameter="Soft", renvoie la version
/// « douce » (fond de pastille à faible opacité).
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value switch
        {
            DiagnosticStatus.Ok => "Ok",
            DiagnosticStatus.NeedsAttention => "Warning",
            DiagnosticStatus.Info => "Info",
            _ => "Danger",
        };
        var soft = parameter is string p && p.Equals("Soft", StringComparison.OrdinalIgnoreCase);
        return Application.Current.TryFindResource($"Brush.{name}{(soft ? "Soft" : "")}") as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
