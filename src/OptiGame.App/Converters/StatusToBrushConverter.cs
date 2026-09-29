using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OptiGame.Core.Diagnostics;

namespace OptiGame.App.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)));
    private static readonly Brush NeedsAttention = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)));
    private static readonly Brush Info = Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)));
    private static readonly Brush Error = Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DiagnosticStatus.Ok => Ok,
        DiagnosticStatus.NeedsAttention => NeedsAttention,
        DiagnosticStatus.Info => Info,
        _ => Error,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
