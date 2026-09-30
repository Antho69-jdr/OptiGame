using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using OptiGame.App.ViewModels;
using OptiGame.Core.Dock;
using OptiGame.Core.Settings;
using OptiGame.Platform.Display;

namespace OptiGame.App.Dock;

/// <summary>
/// Dock flottant. Fenêtre transparente toujours au premier plan, sans focus (WS_EX_NOACTIVATE) ni entrée Alt+Tab
/// (WS_EX_TOOLWINDOW). Les animations ne tournent que pendant une transition (apparition, retrait, grossissement qui
/// n'a pas encore rejoint sa cible, glisser-déposer, onde de clic) : au repos, aucun calcul par image.
/// </summary>
public partial class DockWindow : Window
{
    private const double MaxScale = 1.6;
    private const double CellMargin = 10;      // 5 px de chaque côté d'une icône
    private const double ShelfPadding = 10;   // marge de la rangée (5) + marge d'une cellule (5)
    private const double RowMargin = 5;
    private const double EdgeGap = 6;          // espace entre le dock et le bord de l'écran
    private const double IconRadius = 14;
    private const double SpringOmega = 22;     // grossissement : ~0,25 s pour rejoindre la cible
    private const double DragThreshold = 6;
    private const double LiftScale = 1.08;     // jaquette « soulevée » pendant un glisser

    private static readonly Duration SlideDuration = TimeSpan.FromMilliseconds(170);
    private static readonly Duration ShiftDuration = TimeSpan.FromMilliseconds(200);
    private static readonly IEasingFunction Ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Smooth = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly DockViewModel _vm;
    private readonly FullscreenWatcher _fullscreen;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private DockEdge _edge = DockEdge.Bottom;
    private bool _autoHide = true;
    private bool _shown;
    private bool _suppressed;

    // Grossissement : position de la souris le long du dock (null = icônes au repos), vitesse de chaque icône.
    private double? _magnifyAxis;
    private readonly ConditionalWeakTable<FrameworkElement, StrongBox<double>> _velocities = new();
    private TimeSpan? _lastFrame;
    private bool _magnifying;

    private Press? _press;
    private bool _dragging;

    /// <summary>Jaquette pressée (clic à venir) ou tenue (glisser en cours).</summary>
    private sealed class Press(DockItemViewModel item, FrameworkElement cell, int from, Point start)
    {
        public DockItemViewModel Item { get; } = item;

        public FrameworkElement Cell { get; } = cell;

        public int From { get; } = from;

        public Point Start { get; } = start;

        public int Target { get; set; } = from;

        public bool Dragging { get; set; }
    }

    public DockWindow(DockViewModel vm, FullscreenWatcher fullscreen)
    {
        InitializeComponent();
        _vm = vm;
        _fullscreen = fullscreen;
        DataContext = vm;
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_autoHide && !_dragging && !Shelf.IsMouseOver) SlideOut();
        };
    }

    public static readonly DependencyProperty IconWidthProperty =
        DependencyProperty.Register(nameof(IconWidth), typeof(double), typeof(DockWindow), new PropertyMetadata(64.0));

    public static readonly DependencyProperty IconHeightProperty =
        DependencyProperty.Register(nameof(IconHeight), typeof(double), typeof(DockWindow), new PropertyMetadata(64.0));

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(DockWindow), new PropertyMetadata(Orientation.Horizontal));

    public static readonly DependencyProperty TipPlacementProperty =
        DependencyProperty.Register(nameof(TipPlacement), typeof(PlacementMode), typeof(DockWindow), new PropertyMetadata(PlacementMode.Top));

    public double IconWidth
    {
        get => (double)GetValue(IconWidthProperty);
        set => SetValue(IconWidthProperty, value);
    }

    public double IconHeight
    {
        get => (double)GetValue(IconHeightProperty);
        set => SetValue(IconHeightProperty, value);
    }

    /// <summary>Dimension d'une icône le long du dock (espacement, grossissement).</summary>
    private double Along => Horizontal ? IconWidth : IconHeight;

    /// <summary>Dimension d'une icône en travers du dock (épaisseur du plateau).</summary>
    private double Across => Horizontal ? IconHeight : IconWidth;

    /// <summary>Place d'une icône au repos le long du dock, marges comprises.</summary>
    private double Slot => Along + CellMargin;

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public PlacementMode TipPlacement
    {
        get => (PlacementMode)GetValue(TipPlacementProperty);
        set => SetValue(TipPlacementProperty, value);
    }

    private bool Horizontal => _edge is DockEdge.Bottom or DockEdge.Top;

    // ---- Réglages et placement ----

    public void ApplySettings(DockEdge edge, int iconSize, DockIconShape shape, double opacity, bool autoHide)
    {
        _edge = edge;
        _autoHide = autoHide;
        (IconWidth, IconHeight) = DockLayout.IconSize(iconSize, shape);
        var (background, border) = DockLayout.PlateAlpha(opacity);
        Plate.Background = new SolidColorBrush(Color.FromArgb(background, 0x12, 0x16, 0x20));
        Plate.BorderBrush = new SolidColorBrush(Color.FromArgb(border, 0xFF, 0xFF, 0xFF));
        Orientation = Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        Row.Orientation = Orientation;
        TipPlacement = edge switch
        {
            DockEdge.Top => PlacementMode.Bottom,
            DockEdge.Left => PlacementMode.Right,
            DockEdge.Right => PlacementMode.Left,
            _ => PlacementMode.Top,
        };

        // Fenêtre : toute la longueur du bord (zone de travail de l'écran principal, hors barre des tâches),
        // épaisse de la taille d'une icône agrandie + place pour l'info-bulle.
        var area = SystemParameters.WorkArea;
        var thickness = Across * MaxScale + ShelfPadding * 2 + EdgeGap + 40;
        switch (edge)
        {
            case DockEdge.Top:
                (Left, Top, Width, Height) = (area.Left, area.Top, area.Width, thickness);
                break;
            case DockEdge.Left:
                (Left, Top, Width, Height) = (area.Left, area.Top, thickness, area.Height);
                break;
            case DockEdge.Right:
                (Left, Top, Width, Height) = (area.Right - thickness, area.Top, thickness, area.Height);
                break;
            default:
                (Left, Top, Width, Height) = (area.Left, area.Bottom - thickness, area.Width, thickness);
                break;
        }

        // Étagère collée au bord, centrée ; plateau épais d'une icône au repos (les icônes agrandies en débordent).
        var plate = Across + ShelfPadding * 2;
        Shelf.HorizontalAlignment = edge switch { DockEdge.Left => HorizontalAlignment.Left, DockEdge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        Shelf.VerticalAlignment = edge switch { DockEdge.Top => VerticalAlignment.Top, DockEdge.Bottom => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
        Shelf.Margin = edge switch
        {
            DockEdge.Top => new Thickness(0, EdgeGap, 0, 0),
            DockEdge.Left => new Thickness(EdgeGap, 0, 0, 0),
            DockEdge.Right => new Thickness(0, 0, EdgeGap, 0),
            _ => new Thickness(0, 0, 0, EdgeGap),
        };
        Plate.Width = Horizontal ? double.NaN : plate;
        Plate.Height = Horizontal ? plate : double.NaN;
        Plate.HorizontalAlignment = Shelf.HorizontalAlignment == HorizontalAlignment.Center ? HorizontalAlignment.Stretch : Shelf.HorizontalAlignment;
        Plate.VerticalAlignment = Shelf.VerticalAlignment == VerticalAlignment.Center ? VerticalAlignment.Stretch : Shelf.VerticalAlignment;
        Row.HorizontalAlignment = Plate.HorizontalAlignment == HorizontalAlignment.Stretch ? HorizontalAlignment.Center : Plate.HorizontalAlignment;
        Row.VerticalAlignment = Plate.VerticalAlignment == VerticalAlignment.Stretch ? VerticalAlignment.Center : Plate.VerticalAlignment;

        Divider.Width = Horizontal ? 1 : Across * 0.7;
        Divider.Height = Horizontal ? Across * 0.7 : 1;
        Divider.Margin = Horizontal ? new Thickness(6, 0, 6, 0) : new Thickness(0, 6, 0, 6);

        Trigger.Width = Horizontal ? double.NaN : 2;
        Trigger.Height = Horizontal ? 2 : double.NaN;
        Trigger.HorizontalAlignment = edge == DockEdge.Right ? HorizontalAlignment.Right : edge == DockEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Trigger.VerticalAlignment = edge == DockEdge.Top ? VerticalAlignment.Top : edge == DockEdge.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        Trigger.Visibility = autoHide ? Visibility.Visible : Visibility.Collapsed;

        if (autoHide) SlideOut(animate: false);
        else SlideIn(animate: false);
    }

    /// <summary>Masque complètement le dock (partie en cours, application plein écran).</summary>
    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        Visibility = suppressed ? Visibility.Hidden : Visibility.Visible;
        if (!suppressed && _autoHide) SlideOut(animate: false);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    // ---- Apparition / retrait ----

    private void OnTriggerEnter(object sender, MouseEventArgs e)
    {
        // Un jeu a pu passer en plein écran après avoir pris le premier plan : on revérifie avant d'apparaître.
        if (_suppressed || _fullscreen.Evaluate()) return;
        SlideIn();
    }

    private void OnShelfEnter(object sender, MouseEventArgs e) => _hideTimer.Stop();

    private void OnShelfLeave(object sender, MouseEventArgs e)
    {
        if (_press is not null) return; // souris capturée par une jaquette : le relâchement décidera
        Magnify(null);
        if (_autoHide) _hideTimer.Start();
    }

    private void SlideIn(bool animate = true)
    {
        _shown = true;
        AnimateSlide(0, animate);
    }

    private void SlideOut(bool animate = true)
    {
        _shown = false;
        var distance = Across + ShelfPadding * 2 + EdgeGap + 12;
        AnimateSlide(_edge is DockEdge.Top or DockEdge.Left ? -distance : distance, animate);
    }

    private void AnimateSlide(double to, bool animate)
    {
        var property = Horizontal ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        var other = Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        Slide.BeginAnimation(other, null);
        Slide.SetValue(other, 0.0);
        if (!animate)
        {
            Slide.BeginAnimation(property, null);
            Slide.SetValue(property, to);
            return;
        }
        Slide.BeginAnimation(property, new DoubleAnimation(to, SlideDuration) { EasingFunction = Ease });
    }

    // ---- Grossissement façon macOS ----

    private void OnShelfMouseMove(object sender, MouseEventArgs e)
    {
        if (!_shown || _dragging) return;
        var mouse = e.GetPosition(Root);
        Magnify(Horizontal ? mouse.X : mouse.Y);
    }

    /// <summary>
    /// Change la cible du grossissement. Les icônes la rejoignent image par image avec un ressort (départ doux,
    /// arrivée ralentie, dans les deux sens), puis la boucle s'arrête d'elle-même.
    /// </summary>
    private void Magnify(double? axis)
    {
        _magnifyAxis = axis;
        if (_magnifying) return;
        _magnifying = true;
        _lastFrame = null;
        CompositionTarget.Rendering += OnMagnifyFrame;
    }

    private void OnMagnifyFrame(object? sender, EventArgs e)
    {
        // Heure de l'image à afficher (et non l'heure courante) : pas régulier même si l'événement arrive en retard.
        // WPF peut le déclencher plusieurs fois pour la même image : on n'avance qu'une fois.
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        var elapsed = _lastFrame is { } last ? Math.Min((now - last).TotalSeconds, 0.05) : 1 / 60.0; // après un gel, pas de saut
        _lastFrame = now;

        var cells = Cells().ToList();
        var targets = TargetScales(cells.Count);
        var settled = true;
        for (var i = 0; i < cells.Count; i++)
        {
            var transform = LayoutScale(cells[i]);
            var velocity = _velocities.GetValue(cells[i], _ => new StrongBox<double>());
            var (scale, speed) = DockSpring.Step(transform.ScaleX, velocity.Value, targets[i], elapsed, SpringOmega);
            if (scale != transform.ScaleX) transform.ScaleX = transform.ScaleY = scale;
            velocity.Value = speed;
            settled &= scale == targets[i] && speed == 0;
        }

        if (settled)
        {
            CompositionTarget.Rendering -= OnMagnifyFrame;
            _magnifying = false;
        }
    }

    /// <summary>Grossissement visé pour chaque cellule (jeux puis OptiGame) selon la position de la souris.</summary>
    private double[] TargetScales(int count)
    {
        var targets = Enumerable.Repeat(1.0, count).ToArray();
        if (_magnifyAxis is not { } axis) return targets;

        // Centres calculés sur la disposition AU REPOS, dans le repère de la fenêtre : stable même pendant
        // que les icônes grossissent et écartent leurs voisines.
        var extra = Horizontal ? Divider.Width + Divider.Margin.Left + Divider.Margin.Right : Divider.Height + Divider.Margin.Top + Divider.Margin.Bottom;
        var length = Horizontal ? Root.ActualWidth : Root.ActualHeight;
        var total = count * Slot + extra + (_vm.IsEmpty ? EmptyHint.ActualWidth : 0);
        var start = (length - total - RowMargin * 2) / 2 + RowMargin;
        var range = Along * 2.2;
        for (var i = 0; i < count; i++)
        {
            var isOg = i == count - 1;
            var center = start + i * Slot + Slot / 2 + (isOg ? extra : 0);
            targets[i] = DockMagnification.Scale(axis - center, range, MaxScale);
        }
        return targets;
    }

    private static ScaleTransform LayoutScale(FrameworkElement cell)
    {
        // Un ScaleTransform déclaré dans un DataTemplate est gelé (partagé par toutes les instances du modèle) :
        // le modifier lève une exception. Chaque cellule reçoit donc son propre transform, modifiable.
        if (cell.LayoutTransform is not ScaleTransform { IsFrozen: false } transform)
        {
            transform = new ScaleTransform(1, 1);
            cell.LayoutTransform = transform;
        }
        return transform;
    }

    /// <summary>Cellules des jeux dans l'ordre, puis celle d'OptiGame.</summary>
    private IEnumerable<FrameworkElement> Cells()
    {
        foreach (var (_, cell, _) in GameCells()) yield return cell;
        yield return OgCell;
    }

    private IEnumerable<(DockItemViewModel Item, FrameworkElement Cell, ContentPresenter Presenter)> GameCells()
    {
        for (var i = 0; i < ItemsHost.Items.Count; i++)
        {
            if (ItemsHost.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter presenter &&
                VisualTreeHelper.GetChildrenCount(presenter) > 0 &&
                VisualTreeHelper.GetChild(presenter, 0) is FrameworkElement { DataContext: DockItemViewModel item } cell)
            {
                yield return (item, cell, presenter);
            }
        }
    }

    // ---- Clic (onde puis lancement) et glisser pour réordonner ----

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } cell) return;
        _press = new Press(item, cell, _vm.Items.IndexOf(item), e.GetPosition(Root));
        cell.CaptureMouse(); // la suite du geste (glisser, relâcher hors de la jaquette) revient à cette cellule
        AnimateLift(cell, 0.94, TimeSpan.FromMilliseconds(90), Smooth); // léger enfoncement
        e.Handled = true;
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_press is not { } press || !ReferenceEquals(sender, press.Cell)) return;
        var delta = e.GetPosition(Root) - press.Start;
        if (!press.Dragging)
        {
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;
            BeginDrag(press);
        }

        var count = _vm.Items.Count;
        var offset = DockReorder.ClampOffset(press.From, Horizontal ? delta.X : delta.Y, Slot, count);
        MoveCell(press.Cell, offset, animate: false);
        var target = DockReorder.TargetIndex(press.From, offset, Slot, count);
        if (target == press.Target) return;
        press.Target = target;
        foreach (var (index, cell) in GameCells().Select((c, i) => (i, c.Cell)))
        {
            if (!ReferenceEquals(cell, press.Cell)) MoveCell(cell, DockReorder.Shift(index, press.From, target, Slot), animate: true);
        }
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_press is not { } press || !ReferenceEquals(sender, press.Cell)) return;
        _press = null; // avant de relâcher la capture : OnItemLostCapture ne doit plus rien faire
        press.Cell.ReleaseMouseCapture();
        e.Handled = true;
        LeaveIfMouseOutside();

        if (press.Dragging)
        {
            EndDrag(press, commit: true);
            return;
        }

        AnimateLift(press.Cell, 1, TimeSpan.FromMilliseconds(260), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
        var at = e.GetPosition(press.Cell);
        if (!new Rect(press.Cell.RenderSize).Contains(at)) return; // relâché en dehors : clic annulé

        Ripple(press.Cell, at);
        // Lancement juste après le premier rendu de l'onde (Background < Render) : elle démarre sans attendre.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _vm.LaunchCommand.Execute(press.Item));
    }

    private void OnItemLostCapture(object sender, MouseEventArgs e)
    {
        // Capture perdue sans relâchement (Alt+Tab, fenêtre système…) : on annule proprement.
        if (_press is not { } press || !ReferenceEquals(sender, press.Cell)) return;
        _press = null;
        if (press.Dragging) EndDrag(press, commit: false);
        else AnimateLift(press.Cell, 1, TimeSpan.FromMilliseconds(150), Smooth);
        LeaveIfMouseOutside();
    }

    /// <summary>Pendant la capture, la sortie de la souris n'a pas été traitée : on la rattrape au relâchement.</summary>
    private void LeaveIfMouseOutside()
    {
        if (Shelf.IsMouseOver) return;
        Magnify(null);
        if (_autoHide) _hideTimer.Start();
    }

    private void BeginDrag(Press press)
    {
        press.Dragging = true;
        _dragging = true;
        _hideTimer.Stop();
        Magnify(null); // les icônes reprennent leur taille : les cases gardent une largeur fixe pendant le glisser
        foreach (var (_, cell, presenter) in GameCells())
        {
            Panel.SetZIndex(presenter, ReferenceEquals(cell, press.Cell) ? 1 : 0); // la jaquette tenue passe au-dessus
            ToolTipService.SetIsEnabled(cell, false);
            if (cell.ToolTip is ToolTip { IsOpen: true } tip) tip.IsOpen = false;
        }
        AnimateLift(press.Cell, LiftScale, TimeSpan.FromMilliseconds(150), Smooth);
        press.Cell.Opacity = 0.92;
    }

    /// <summary>
    /// Fin du glisser : l'ordre est appliqué, puis chaque jaquette glisse de sa position à l'écran vers sa nouvelle case
    /// (on mesure avant, on réordonne, on repart de l'écart : pas de saut, même si la cellule a été recréée).
    /// </summary>
    private void EndDrag(Press press, bool commit)
    {
        _dragging = false;
        var before = GameCells().ToDictionary(c => c.Item.Id, c => SlotPosition(c.Presenter) + CellOffset(c.Cell));

        if (commit && press.Target != press.From) _vm.Move(press.Item.Id, press.Target);
        UpdateLayout();

        foreach (var (item, cell, presenter) in GameCells())
        {
            var from = before.TryGetValue(item.Id, out var old) ? old - SlotPosition(presenter) : 0;
            MoveCell(cell, from, animate: false);
            MoveCell(cell, 0, animate: true);
            ToolTipService.SetIsEnabled(cell, true);
            Panel.SetZIndex(presenter, item.Id == press.Item.Id ? 1 : 0);
            if (item.Id == press.Item.Id)
            {
                cell.Opacity = 1;
                AnimateLift(cell, 1, ShiftDuration.TimeSpan, Smooth);
            }
        }
    }

    private double SlotPosition(ContentPresenter presenter)
    {
        var offset = VisualTreeHelper.GetOffset(presenter);
        return Horizontal ? offset.X : offset.Y;
    }

    private double CellOffset(FrameworkElement cell)
    {
        var move = RenderParts(cell).Move;
        return Horizontal ? move.X : move.Y;
    }

    /// <summary>Décale une cellule le long du dock (sans toucher à la disposition des autres).</summary>
    private void MoveCell(FrameworkElement cell, double offset, bool animate)
    {
        var move = RenderParts(cell).Move;
        var property = Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var other = Horizontal ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        move.BeginAnimation(other, null);
        move.SetValue(other, 0.0);
        if (animate)
        {
            move.BeginAnimation(property, new DoubleAnimation(offset, ShiftDuration) { EasingFunction = Smooth });
            return;
        }
        move.BeginAnimation(property, null);
        move.SetValue(property, offset);
    }

    private static void AnimateLift(FrameworkElement cell, double scale, TimeSpan duration, IEasingFunction easing)
    {
        var lift = RenderParts(cell).Lift;
        var animation = new DoubleAnimation(scale, duration) { EasingFunction = easing };
        lift.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        lift.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>Soulèvement (échelle autour du centre) puis déplacement : une instance par cellule, jamais gelée.</summary>
    private static (ScaleTransform Lift, TranslateTransform Move) RenderParts(FrameworkElement cell)
    {
        if (cell.RenderTransform is TransformGroup { IsFrozen: false, Children: [ScaleTransform lift, TranslateTransform move] })
        {
            return (lift, move);
        }
        var parts = (Lift: new ScaleTransform(1, 1), Move: new TranslateTransform());
        cell.RenderTransform = new TransformGroup { Children = { parts.Lift, parts.Move } };
        return parts;
    }

    /// <summary>Onde lumineuse qui part du point de clic et s'efface, découpée à la forme arrondie de la jaquette.</summary>
    private static void Ripple(FrameworkElement cell, Point at)
    {
        if (cell is not Panel { Children.Count: > 0 } panel || panel.Children[0] is not Grid icon) return;
        at = cell.TranslatePoint(at, icon);
        var size = icon.RenderSize;
        icon.Clip = new RectangleGeometry(new Rect(size), IconRadius, IconRadius);

        // Rayon : jusqu'au coin le plus éloigné, pour que l'onde traverse toute la jaquette.
        var radius = new[] { new Point(0, 0), new Point(size.Width, 0), new Point(0, size.Height), new Point(size.Width, size.Height) }
            .Max(corner => (corner - at).Length);
        var white = Color.FromRgb(0xFF, 0xFF, 0xFF);
        var wave = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(at.X - radius, at.Y - radius, 0, 0),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x00, white.R, white.G, white.B), 0),
                new(Color.FromArgb(0x26, white.R, white.G, white.B), 0.55),
                new(Color.FromArgb(0x9A, white.R, white.G, white.B), 0.86),
                new(Color.FromArgb(0x40, white.R, white.G, white.B), 0.93),
                new(Color.FromArgb(0x00, white.R, white.G, white.B), 1),
            }),
        };
        var grow = new ScaleTransform(0.05, 0.05);
        wave.RenderTransform = grow;
        icon.Children.Add(wave);

        var duration = TimeSpan.FromMilliseconds(650);
        var expand = new DoubleAnimation(0.05, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => icon.Children.Remove(wave);
        grow.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
        grow.BeginAnimation(ScaleTransform.ScaleYProperty, expand);
        wave.BeginAnimation(OpacityProperty, fade);
    }

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DockItemViewModel item) return;
        _hideTimer.Stop();
        var menu = new ContextMenu
        {
            Items =
            {
                MenuItem("Jouer", () => _vm.LaunchCommand.Execute(item)),
                MenuItem("Ouvrir la page du jeu", () => _vm.OpenGameCommand.Execute(item)),
                new Separator(),
                MenuItem("Retirer du dock", () => _vm.UnpinCommand.Execute(item)),
            },
            PlacementTarget = (UIElement)sender,
            Placement = TipPlacement,
        };
        menu.Closed += (_, _) =>
        {
            if (_autoHide && !Shelf.IsMouseOver) _hideTimer.Start();
        };
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnOptiGameClick(object sender, MouseButtonEventArgs e) => _vm.OpenOptiGameCommand.Execute(null);

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // ---- Styles de fenêtre Win32 ----

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
