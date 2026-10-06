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
    private const double EdgeGap = 6;          // espace entre le dock et le bord de l'écran
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
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _menuOpen;

    private DockEdge _edge = DockEdge.Bottom;
    private bool _autoHide = true;
    private bool _shown;
    private bool _suppressed;
    private bool _pinnedToDesktop;
    private bool _desktopShown;
    private bool _showNames = true;

    // Grossissement : dernière position de la souris le long du dock (repère de la rangée) et intensité de l'effet
    // (0 = icônes au repos, 1 = grossissement complet), animée par un ressort.
    private double? _magnifyAxis;
    private double _intensity;
    private double _intensityVelocity;
    private double _intensityTarget;
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
            HideIfIdle();
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            HideIfIdle();
        };
    }

    /// <summary>Animations activées (Paramètres › Général, selon Windows par défaut) : sinon le dock apparaît sans glisser, sans ressort ni onde.</summary>
    private static bool Animations => Services.UiMotion.Enabled;

    /// <summary>Masquage automatique : se range sauf si la souris est dessus, sur la bande du bord, ou dans un menu ouvert.</summary>
    private void HideIfIdle()
    {
        if (_autoHide && !_dragging && !_menuOpen && !Shelf.IsMouseOver && !Trigger.IsMouseOver) SlideOut();
    }

    public static readonly DependencyProperty IconCornerRadiusProperty =
        DependencyProperty.Register(nameof(IconCornerRadius), typeof(CornerRadius), typeof(DockWindow), new PropertyMetadata(new CornerRadius(14)));

    public static readonly DependencyProperty PlateCornerRadiusProperty =
        DependencyProperty.Register(nameof(PlateCornerRadius), typeof(CornerRadius), typeof(DockWindow), new PropertyMetadata(new CornerRadius(20)));

    /// <summary>Arrondi des icônes : réglage DockCornerRadius (14 px par défaut, quelle que soit leur taille) ; plateau 6 px de plus.</summary>
    public CornerRadius IconCornerRadius
    {
        get => (CornerRadius)GetValue(IconCornerRadiusProperty);
        set => SetValue(IconCornerRadiusProperty, value);
    }

    public CornerRadius PlateCornerRadius
    {
        get => (CornerRadius)GetValue(PlateCornerRadiusProperty);
        set => SetValue(PlateCornerRadiusProperty, value);
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

    public void ApplySettings(AppSettings settings)
    {
        var edge = settings.DockEdge;
        var autoHide = settings.DockAutoHide;
        _edge = edge;
        _autoHide = autoHide;
        _showNames = settings.DockShowNames;
        _hideTimer.Interval = TimeSpan.FromSeconds(DockLayout.HideDelay(settings.DockHideDelay));
        OgCell.Visibility = Divider.Visibility = settings.DockShowOptiGame ? Visibility.Visible : Visibility.Collapsed;
        (IconWidth, IconHeight) = DockLayout.IconSize(settings.DockIconSize, settings.DockIconShape);
        var radius = DockLayout.CornerRadius(settings.DockCornerRadius);
        (IconCornerRadius, PlateCornerRadius) = (new CornerRadius(radius), new CornerRadius(radius + 6)); // plateau : 6 px de plus (14 → 20)
        var (background, border) = DockLayout.PlateAlpha(settings.DockOpacity);
        var plateColor = (Color)FindResource("Color.DockPlate");
        var highlight = (Color)FindResource("Color.Highlight");
        Plate.Background = new SolidColorBrush(Color.FromArgb(background, plateColor.R, plateColor.G, plateColor.B));
        Plate.BorderBrush = new SolidColorBrush(Color.FromArgb(border, highlight.R, highlight.G, highlight.B));
        Orientation = Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        Row.Orientation = Orientation;
        TipPlacement = edge switch
        {
            DockEdge.Top => PlacementMode.Bottom,
            DockEdge.Left => PlacementMode.Right,
            DockEdge.Right => PlacementMode.Left,
            _ => PlacementMode.Top,
        };

        PlaceWindow();

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
        Plate.Margin = new Thickness(0);
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

        // Masquage automatique : au premier plan (il n'apparaît que sur demande). Sinon : collé au bureau, derrière
        // toutes les fenêtres, comme un widget ; OnWindowMessage l'y maintient.
        _pinnedToDesktop = !autoHide;
        _desktopShown = false;
        Topmost = autoHide;
        if (_pinnedToDesktop) PinToDesktop();
    }

    /// <summary>
    /// Fenêtre : toute la longueur du bord (zone de travail de l'écran principal, hors barre des tâches), épaisse de la
    /// taille d'une icône agrandie + place pour l'info-bulle. Recalculée quand l'écran, sa mise à l'échelle ou la barre
    /// des tâches change.
    /// </summary>
    private void PlaceWindow()
    {
        var area = SystemParameters.WorkArea;
        var thickness = Across * MaxScale + ShelfPadding * 2 + EdgeGap + 40;
        switch (_edge)
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
    }

    /// <summary>
    /// Réglage du dock modifié : il se montre 2 s pour qu'on en voie l'effet, puis se range s'il se masque automatiquement
    /// (sauf si la souris est dessus).
    /// </summary>
    public void Preview()
    {
        if (_suppressed || !_autoHide) return;
        SlideIn();
        _hideTimer.Stop();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    /// <summary>
    /// « Afficher le bureau » (Win+D) : le bureau passe devant les applications et cacherait le dock collé au bureau.
    /// Tant qu'il est affiché, le dock passe au premier plan ; il se recolle au bureau dès qu'une application revient.
    /// </summary>
    public void SetDesktopShown(bool shown)
    {
        if (!_pinnedToDesktop || shown == _desktopShown) return;
        _desktopShown = shown; // avant Topmost : OnWindowMessage ne doit plus (ou de nouveau) rediriger la position
        Topmost = shown;
        if (!shown) PinToDesktop();
    }

    private void PinToDesktop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        // Le vrai emplacement (juste au-dessus du bureau) est calculé dans OnWindowMessage.
        SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Fenêtre fermée (dock désactivé, ou partie en cours) : rien ne doit plus la garder en vie.</summary>
    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnMagnifyFrame; // événement statique : garderait toute la fenêtre
        _magnifying = false;
        _hideTimer.Stop();
        _previewTimer.Stop();
        base.OnClosed(e);
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
        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
    }

    /// <summary>
    /// Dock collé au bureau : toute modification de sa place dans la pile des fenêtres est redirigée juste au-dessus
    /// du bureau. Pas « tout en bas » (HWND_BOTTOM) : il passerait sous le bureau lui-même (Progman), invisible.
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Résolution, mise à l'échelle ou barre des tâches modifiées : le dock reprend sa place le long du bord, une fois
        // que WPF a relu les nouvelles valeurs (SystemParameters.WorkArea).
        if (msg is WmDisplayChange or WmDpiChanged || (msg == WmSettingChange && wParam == SpiSetWorkArea))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, PlaceWindow);
            return IntPtr.Zero;
        }
        if (msg != WmWindowPosChanging || !_pinnedToDesktop || _desktopShown) return IntPtr.Zero;
        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        if ((pos.Flags & SwpNoZOrder) != 0) return IntPtr.Zero;
        pos.InsertAfter = LowestWindowAboveDesktop(hwnd);
        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    /// <summary>Fenêtre la plus basse hors bureau et hors dock : le dock se place juste en dessous d'elle.</summary>
    private static IntPtr LowestWindowAboveDesktop(IntPtr self)
    {
        var window = GetWindow(self, GwHwndLast);
        while (window != IntPtr.Zero && (window == self || IsDesktop(window))) window = GetWindow(window, GwHwndPrev);
        return window == IntPtr.Zero ? HwndTop : window;
    }

    private static bool IsDesktop(IntPtr window)
    {
        var name = new System.Text.StringBuilder(32);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW";
    }

    // ---- Apparition / retrait ----

    private void OnTriggerEnter(object sender, MouseEventArgs e)
    {
        // Un jeu a pu passer en plein écran après avoir pris le premier plan : on revérifie avant d'apparaître.
        if (_suppressed || _fullscreen.Evaluate()) return;
        _hideTimer.Stop();
        SlideIn();
    }

    /// <summary>Souris passée par le bord sans aller jusqu'au dock (vers un autre écran, la barre des tâches…) : il se range.</summary>
    private void OnTriggerLeave(object sender, MouseEventArgs e)
    {
        if (_shown && _autoHide && !Shelf.IsMouseOver) _hideTimer.Start();
    }

    private void OnShelfEnter(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop();
        _previewTimer.Stop();
    }

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
        if (!animate || !Animations)
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
        var mouse = e.GetPosition(Row);
        Magnify(Horizontal ? mouse.X : mouse.Y);
    }

    /// <summary>
    /// Suit la souris (null = elle est partie). La position est suivie telle quelle ; seule l'intensité de l'effet
    /// est animée, par un ressort (départ doux, arrivée ralentie, dans les deux sens), puis la boucle s'arrête.
    /// </summary>
    private void Magnify(double? axis)
    {
        if (axis is { } position) _magnifyAxis = position;
        _intensityTarget = axis is null ? 0 : 1;
        if (_magnifying) return; // la boucle en cours appliquera la nouvelle position à la prochaine image
        if (!Animations)
        {
            // Sans effets d'animation : l'effet suit la souris sans ressort (aucune boucle par image).
            (_intensity, _intensityVelocity) = (_intensityTarget, 0);
            ApplyMagnification();
            return;
        }
        if (_intensity == _intensityTarget)
        {
            if (_intensity > 0) ApplyMagnification();
            return;
        }
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

        (_intensity, _intensityVelocity) = DockSpring.Step(_intensity, _intensityVelocity, _intensityTarget, elapsed, SpringOmega);
        ApplyMagnification();
        if (_intensity == _intensityTarget && _intensityVelocity == 0)
        {
            CompositionTarget.Rendering -= OnMagnifyFrame;
            _magnifying = false;
        }
    }

    /// <summary>
    /// Place chaque élément selon l'étirement de l'axe autour de la souris (<see cref="DockMagnification"/>), par
    /// transformations de rendu uniquement : la disposition au repos, donc le centrage du dock, ne bouge jamais.
    /// Le plateau s'allonge de chaque côté de ce que les icônes ont gagné.
    /// </summary>
    private void ApplyMagnification()
    {
        var mouse = _magnifyAxis ?? 0;
        var maxScale = 1 + (MaxScale - 1) * Math.Max(0, _intensity);
        var range = Along * 2.2;
        var cells = Cells().ToList();

        foreach (var cell in cells)
        {
            var (scale, shift) = DockMagnification.Place(RestStart(cell) - CellMargin / 2, Slot, mouse, range, maxScale);
            var parts = RenderParts(cell);
            // Grossit depuis le bord de l'écran, centré le long du dock.
            parts.MagScale.CenterX = Horizontal ? cell.ActualWidth / 2 : _edge == DockEdge.Right ? cell.ActualWidth : 0;
            parts.MagScale.CenterY = Horizontal ? (_edge == DockEdge.Bottom ? cell.ActualHeight : 0) : cell.ActualHeight / 2;
            parts.MagScale.ScaleX = parts.MagScale.ScaleY = scale;
            if (Horizontal) (parts.MagShift.X, parts.MagShift.Y) = (shift, 0);
            else (parts.MagShift.X, parts.MagShift.Y) = (0, shift);
        }

        foreach (var element in new FrameworkElement[] { Divider, EmptyHint })
        {
            var center = RestStart(element) + (Horizontal ? element.ActualWidth : element.ActualHeight) / 2;
            var shift = DockMagnification.Map(center, mouse, range, maxScale) - center;
            if (element.RenderTransform is not TranslateTransform { IsFrozen: false } move) element.RenderTransform = move = new TranslateTransform();
            (move.X, move.Y) = Horizontal ? (shift, 0.0) : (0.0, shift);
        }

        if (cells.Count == 0) return;
        var first = RestStart(cells[0]) - CellMargin / 2;
        var end = RestStart(cells[^1]) + Along + CellMargin / 2;
        var before = first - DockMagnification.Map(first, mouse, range, maxScale);
        var after = DockMagnification.Map(end, mouse, range, maxScale) - end;
        Plate.Margin = Horizontal ? new Thickness(-before, 0, -after, 0) : new Thickness(0, -before, 0, -after);
    }

    /// <summary>Début d'un élément le long du dock, dans la disposition au repos (sans transformation de rendu).</summary>
    private double RestStart(FrameworkElement element)
    {
        var offset = VisualTreeHelper.GetOffset(element);
        var point = new Point(offset.X, offset.Y);
        if (VisualTreeHelper.GetParent(element) is Visual parent && !ReferenceEquals(parent, Row) && parent.IsDescendantOf(Row))
        {
            point = parent.TransformToAncestor(Row).Transform(point);
        }
        return Horizontal ? point.X : point.Y;
    }

    /// <summary>Cellules des jeux dans l'ordre, puis celle d'OptiGame si elle est affichée.</summary>
    private IEnumerable<FrameworkElement> Cells()
    {
        foreach (var (_, cell, _) in GameCells()) yield return cell;
        if (OgCell.Visibility == Visibility.Visible) yield return OgCell;
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

        if (Animations) Ripple(press.Cell, at);
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
        ApplyMagnification(); // grossissement éventuellement encore en train de retomber : recalculé sur les nouvelles cases

        foreach (var (item, cell, presenter) in GameCells())
        {
            var from = before.TryGetValue(item.Id, out var old) ? old - SlotPosition(presenter) - MagnifyShift(cell) : 0;
            MoveCell(cell, from, animate: false);
            MoveCell(cell, 0, animate: true);
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

    /// <summary>Décalage à l'écran d'une cellule par rapport à sa case : glisser + grossissement.</summary>
    private double CellOffset(FrameworkElement cell)
    {
        var move = RenderParts(cell).Move;
        return (Horizontal ? move.X : move.Y) + MagnifyShift(cell);
    }

    private double MagnifyShift(FrameworkElement cell)
    {
        var shift = RenderParts(cell).MagShift;
        return Horizontal ? shift.X : shift.Y;
    }

    /// <summary>Décale une cellule le long du dock (sans toucher à la disposition des autres).</summary>
    private void MoveCell(FrameworkElement cell, double offset, bool animate)
    {
        var move = RenderParts(cell).Move;
        var property = Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var other = Horizontal ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        move.BeginAnimation(other, null);
        move.SetValue(other, 0.0);
        if (animate && Animations)
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
        (lift.CenterX, lift.CenterY) = (cell.ActualWidth / 2, cell.ActualHeight / 2);
        var animation = new DoubleAnimation(scale, Animations ? duration : TimeSpan.Zero) { EasingFunction = easing };
        lift.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        lift.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>
    /// Transformations de rendu d'une cellule, dans l'ordre : soulèvement / enfoncement (autour de son centre),
    /// grossissement (depuis le bord de l'écran) et son décalage, puis déplacement du glisser. Une instance par
    /// cellule, jamais gelée (un transform déclaré dans un DataTemplate le serait).
    /// </summary>
    private static (ScaleTransform Lift, ScaleTransform MagScale, TranslateTransform MagShift, TranslateTransform Move) RenderParts(FrameworkElement cell)
    {
        if (cell.RenderTransform is TransformGroup { IsFrozen: false, Children: [ScaleTransform lift, ScaleTransform magScale, TranslateTransform magShift, TranslateTransform move] })
        {
            return (lift, magScale, magShift, move);
        }
        var parts = (Lift: new ScaleTransform(1, 1), MagScale: new ScaleTransform(1, 1), MagShift: new TranslateTransform(), Move: new TranslateTransform());
        cell.RenderTransform = new TransformGroup { Children = { parts.Lift, parts.MagScale, parts.MagShift, parts.Move } };
        return parts;
    }

    /// <summary>Onde lumineuse qui part du point de clic et s'efface, découpée à la forme arrondie de la jaquette.</summary>
    private void Ripple(FrameworkElement cell, Point at)
    {
        if (cell is not Panel { Children.Count: > 0 } panel || panel.Children[0] is not Grid icon) return;
        at = cell.TranslatePoint(at, icon);
        var size = icon.RenderSize;
        icon.Clip = new RectangleGeometry(new Rect(size), IconCornerRadius.TopLeft, IconCornerRadius.TopLeft);

        // Rayon : jusqu'au coin le plus éloigné, pour que l'onde traverse toute la jaquette.
        var radius = new[] { new Point(0, 0), new Point(size.Width, 0), new Point(0, size.Height), new Point(size.Width, size.Height) }
            .Max(corner => (corner - at).Length);
        var white = (Color)cell.FindResource("Color.Highlight");
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
        var index = _vm.Items.IndexOf(item);
        var (toStart, toEnd) = Horizontal ? ("Déplacer vers la gauche", "Déplacer vers la droite") : ("Déplacer vers le haut", "Déplacer vers le bas");
        var play = MenuItem("Jouer", () => _vm.LaunchCommand.Execute(item), isEnabled: !item.IsLaunching);
        play.FontWeight = FontWeights.SemiBold; // action du clic
        OpenMenu((UIElement)sender,
            play,
            MenuItem("Ouvrir la fiche", () => _vm.OpenGameCommand.Execute(item)),
            new Separator(),
            MenuItem(toStart, () => _vm.MoveBy(item, -1), isEnabled: index > 0),
            MenuItem(toEnd, () => _vm.MoveBy(item, 1), isEnabled: index < _vm.Items.Count - 1),
            new Separator(),
            MenuItem("Retirer du dock", () => _vm.UnpinCommand.Execute(item)));
        e.Handled = true;
    }

    /// <summary>Clic droit sur le plateau, le logo d'OptiGame ou le dock vide : réglages du dock à portée de main.</summary>
    private void OnShelfRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        var open = MenuItem("Ouvrir OptiGame", () => _vm.OpenOptiGameCommand.Execute(null));
        open.FontWeight = FontWeights.SemiBold;
        var autoHide = MenuItem("Masquer automatiquement", () => _vm.ToggleAutoHideCommand.Execute(null));
        (autoHide.IsCheckable, autoHide.IsChecked) = (true, _vm.AutoHide);
        OpenMenu(Shelf,
            open,
            new Separator(),
            autoHide,
            MenuItem("Paramètres du dock…", () => _vm.OpenSettingsCommand.Execute(null)),
            MenuItem("Désactiver le dock…", () => _vm.DisableCommand.Execute(null)));
        e.Handled = true;
    }

    private void OpenMenu(UIElement target, params Control[] items)
    {
        _hideTimer.Stop();
        _previewTimer.Stop();
        var menu = new ContextMenu { PlacementTarget = target, Placement = target == Shelf ? PlacementMode.MousePoint : TipPlacement };
        foreach (var item in items) menu.Items.Add(item);
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_autoHide && !Shelf.IsMouseOver) _hideTimer.Start();
        };
        _menuOpen = true;
        menu.IsOpen = true;
    }

    private void OnEmptyHintClick(object sender, MouseButtonEventArgs e) => _vm.OpenLibraryCommand.Execute(null);

    /// <summary>Nom au survol : selon le réglage (toujours s'il y a un état à dire : désinstallé…), et jamais pendant un glisser.</summary>
    private void OnTipOpening(object sender, ToolTipEventArgs e)
    {
        var hasStatus = (sender as FrameworkElement)?.DataContext is DockItemViewModel { Status.Length: > 0 };
        if ((!_showNames && !hasStatus) || _dragging) e.Handled = true;
    }

    private void OnOptiGameClick(object sender, MouseButtonEventArgs e) => _vm.OpenOptiGameCommand.Execute(null);

    private static MenuItem MenuItem(string header, Action action, bool isEnabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled };
        item.Click += (_, _) => action();
        return item;
    }

    // ---- Styles de fenêtre Win32 ----

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmWindowPosChanging = 0x0046;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private static readonly IntPtr SpiSetWorkArea = new(0x002F);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint GwHwndLast = 1;
    private const uint GwHwndPrev = 3;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly IntPtr HwndBottom = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int capacity);
}
