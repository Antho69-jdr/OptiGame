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

    // Couleurs du thème (Brush.Series1 / Brush.Series2 : bleu / orange, sans sens de statut ; Brush.Border ; Brush.TextMuted),
    // lues une fois et figées.
    private Brush? _primaryBrush;
    private Brush? _secondaryBrush;
    private Pen? _gridPen;
    private Brush? _labelBrush;
    private Typeface? _labelFont;
    private double _labelSize;

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

    /// <summary>Exposé aux lecteurs d'écran comme une image décrite (AutomationProperties.Name : résumé posé par la vue).</summary>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ChartAutomationPeer(this);

    private sealed class ChartAutomationPeer(FrametimeChart owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(FrametimeChart);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Fond transparent : le graphe prend la couleur de la carte qui le contient (hit-test conservé).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        EnsureThemeResources();
        var plot = new Rect(LeftMargin, 8, Math.Max(0, ActualWidth - LeftMargin - 8), Math.Max(0, ActualHeight - 8 - BottomMargin));
        if (plot.Width < 20 || plot.Height < 20) return;

        var series = new[] { (Primary, _primaryBrush!), (Secondary, _secondaryBrush!) }
            .Where(s => s.Item1 is { Count: > 1 })
            .Select(s => (Data: s.Item1!, Brush: s.Item2))
            .ToList();
        if (series.Count == 0)
        {
            Label(dc, "Aucune mesure sélectionnée", new Point(plot.Left + 8, plot.Top + 8));
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
            dc.DrawLine(_gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            // Pas de libellés qui se chevauchent quand l'échelle est grande.
            if (lastLabelY - y >= 14)
            {
                Label(dc, $"{reference:0.#} ms", new Point(2, y - 8));
                lastLabelY = y;
            }
        }
        dc.DrawLine(_gridPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
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
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _labelFont!, _labelSize, _labelBrush!,
            VisualTreeHelper.GetDpi(this).PixelsPerDip), origin);

    private void EnsureThemeResources()
    {
        if (_primaryBrush is not null) return;
        _primaryBrush = ThemeBrush("Brush.Series1");
        _secondaryBrush = ThemeBrush("Brush.Series2");
        _labelBrush = ThemeBrush("Brush.TextMuted");
        _gridPen = new Pen(ThemeBrush("Brush.Border"), 1);
        _gridPen.Freeze();
        _labelFont = new Typeface((FontFamily)FindResource("Font.Text"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _labelSize = (double)FindResource("FontSize.Caption");
    }

    private Brush ThemeBrush(string key)
    {
        var brush = ((Brush)FindResource(key)).CloneCurrentValue();
        brush.Freeze();
        return brush;
    }
}
