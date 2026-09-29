using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OptiGame.App.Controls;

/// <summary>
/// Graphe des frametimes (ms) en fonction du temps (s), une ou deux séries. Dessin direct, sans bibliothèque :
/// les images sont regroupées par pixel en conservant le min et le max, pour que les saccades restent visibles
/// même sur des captures de plusieurs dizaines de milliers d'images.
/// </summary>
public sealed class FrametimeChart : FrameworkElement
{
    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(
        nameof(Primary), typeof(IReadOnlyList<double>), typeof(FrametimeChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(IReadOnlyList<double>), typeof(FrametimeChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush PrimaryBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)));
    private static readonly Brush SecondaryBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)));
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), 1));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));
    private static readonly Typeface LabelFont = new("Segoe UI");

    /// <summary>Repères usuels : 240, 144, 60 et 30 FPS.</summary>
    private static readonly double[] ReferenceLines = [4.17, 6.94, 16.67, 33.33];

    private const double LeftMargin = 48;
    private const double BottomMargin = 20;

    /// <summary>Capture de référence (« avant »), en bleu.</summary>
    public IReadOnlyList<double>? Primary
    {
        get => (IReadOnlyList<double>?)GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    /// <summary>Capture comparée (« après »), en orange.</summary>
    public IReadOnlyList<double>? Secondary
    {
        get => (IReadOnlyList<double>?)GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        var plot = new Rect(LeftMargin, 8, Math.Max(0, ActualWidth - LeftMargin - 8), Math.Max(0, ActualHeight - 8 - BottomMargin));
        if (plot.Width < 20 || plot.Height < 20) return;

        var series = new[] { (Primary, PrimaryBrush), (Secondary, SecondaryBrush) }
            .Where(s => s.Item1 is { Count: > 1 })
            .Select(s => (Data: s.Item1!, Brush: s.Item2))
            .ToList();
        if (series.Count == 0)
        {
            Label(dc, "Aucune capture sélectionnée", new Point(plot.Left + 8, plot.Top + 8));
            return;
        }

        // Échelle verticale : on écrête les pics extrêmes (99,9e centile × 1,5) pour garder le graphe lisible.
        var all = series.SelectMany(s => s.Data).OrderBy(v => v).ToArray();
        var yMax = Math.Max(10, Math.Min(all[^1], all[(int)((all.Length - 1) * 0.999)] * 1.5));
        var xMax = series.Max(s => s.Data.Sum()) / 1000.0;

        var lastLabelY = double.MaxValue;
        foreach (var reference in ReferenceLines.Where(r => r < yMax))
        {
            var y = plot.Bottom - reference / yMax * plot.Height;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            // Pas de libellés qui se chevauchent quand l'échelle est grande.
            if (lastLabelY - y >= 14)
            {
                Label(dc, $"{reference:0.#} ms", new Point(2, y - 8));
                lastLabelY = y;
            }
        }
        dc.DrawLine(GridPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        Label(dc, "0 s", new Point(plot.Left, plot.Bottom + 2));
        Label(dc, $"{xMax:0} s", new Point(plot.Right - 30, plot.Bottom + 2));

        dc.PushClip(new RectangleGeometry(plot));
        foreach (var (data, brush) in series)
        {
            DrawSeries(dc, data, new Pen(brush, 1), plot, xMax, yMax);
        }
        dc.Pop();
    }

    private static void DrawSeries(DrawingContext dc, IReadOnlyList<double> data, Pen pen, Rect plot, double xMax, double yMax)
    {
        var columns = (int)plot.Width;
        var min = new double[columns];
        var max = new double[columns];
        Array.Fill(min, double.MaxValue);
        Array.Fill(max, double.MinValue);

        var elapsed = 0.0;
        foreach (var frameTime in data)
        {
            elapsed += frameTime / 1000.0;
            var column = Math.Min(columns - 1, (int)(elapsed / xMax * columns));
            min[column] = Math.Min(min[column], frameTime);
            max[column] = Math.Max(max[column], frameTime);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var started = false;
            for (var x = 0; x < columns; x++)
            {
                if (max[x] == double.MinValue) continue;
                var top = new Point(plot.Left + x, plot.Bottom - max[x] / yMax * plot.Height);
                var bottom = new Point(plot.Left + x, plot.Bottom - min[x] / yMax * plot.Height);
                if (!started)
                {
                    context.BeginFigure(bottom, false, false);
                    started = true;
                }
                context.LineTo(bottom, true, false);
                context.LineTo(top, true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void Label(DrawingContext dc, string text, Point origin) =>
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFont, 11, LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip), origin);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
