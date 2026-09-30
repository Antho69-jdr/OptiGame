using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OptiGame.App.ViewModels;
using OptiGame.Core.Settings;
using OptiGame.Platform.Display;

namespace OptiGame.App.Dock;

/// <summary>
/// Dock flottant. Fenêtre transparente toujours au premier plan, sans focus (WS_EX_NOACTIVATE) ni entrée Alt+Tab
/// (WS_EX_TOOLWINDOW). Les animations ne tournent que pendant une transition (apparition, retrait, retour des icônes
/// à leur taille) ; le grossissement n'est recalculé que lorsque la souris bouge au-dessus du dock.
/// </summary>
public partial class DockWindow : Window
{
    private const double MaxScale = 1.6;
    private const double CellMargin = 10;      // 5 px de chaque côté d'une icône
    private const double ShelfPadding = 10;   // marge de la rangée (5) + marge d'une cellule (5)
    private const double RowMargin = 5;
    private const double EdgeGap = 6;          // espace entre le dock et le bord de l'écran
    private const string DragFormat = "OptiGame.DockItem";

    private static readonly Duration SlideDuration = TimeSpan.FromMilliseconds(170);
    private static readonly IEasingFunction Ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

    private readonly DockViewModel _vm;
    private readonly FullscreenWatcher _fullscreen;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private DockEdge _edge = DockEdge.Bottom;
    private bool _autoHide = true;
    private bool _shown;
    private bool _suppressed;

    private DockItemViewModel? _pressedItem;
    private Point _pressPoint;
    private bool _dragging;

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

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(DockWindow), new PropertyMetadata(64.0));

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(DockWindow), new PropertyMetadata(Orientation.Horizontal));

    public static readonly DependencyProperty TipPlacementProperty =
        DependencyProperty.Register(nameof(TipPlacement), typeof(PlacementMode), typeof(DockWindow), new PropertyMetadata(PlacementMode.Top));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

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

    public void ApplySettings(DockEdge edge, int iconSize, bool autoHide)
    {
        _edge = edge;
        _autoHide = autoHide;
        IconSize = Math.Clamp(iconSize, 32, 128);
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
        var thickness = IconSize * MaxScale + ShelfPadding * 2 + EdgeGap + 40;
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
        var plate = IconSize + ShelfPadding * 2;
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

        Divider.Width = Horizontal ? 1 : IconSize * 0.7;
        Divider.Height = Horizontal ? IconSize * 0.7 : 1;
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
        ResetScales();
        if (_autoHide && !_dragging) _hideTimer.Start();
    }

    private void SlideIn(bool animate = true)
    {
        _shown = true;
        AnimateSlide(0, animate);
    }

    private void SlideOut(bool animate = true)
    {
        _shown = false;
        var distance = IconSize + ShelfPadding * 2 + EdgeGap + 12;
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

        // Centres calculés sur la disposition AU REPOS, dans le repère de la fenêtre : stable même pendant
        // que les icônes grossissent et écartent leurs voisines.
        var cells = Cells().ToList();
        var cell = IconSize + CellMargin;
        var extra = Horizontal ? Divider.Width + Divider.Margin.Left + Divider.Margin.Right : Divider.Height + Divider.Margin.Top + Divider.Margin.Bottom;
        var length = Horizontal ? Root.ActualWidth : Root.ActualHeight;
        var total = cells.Count * cell + extra + (_vm.IsEmpty ? EmptyHint.ActualWidth : 0);
        var start = (length - total - RowMargin * 2) / 2 + RowMargin;
        var mouse = e.GetPosition(Root);
        var axis = Horizontal ? mouse.X : mouse.Y;
        var range = IconSize * 2.2;

        for (var i = 0; i < cells.Count; i++)
        {
            var isOg = i == cells.Count - 1;
            var center = start + i * cell + cell / 2 + (isOg ? extra : 0);
            var distance = Math.Abs(axis - center);
            var scale = Core.Dock.DockMagnification.Scale(distance, range, MaxScale);
            SetScale(cells[i], scale, animate: false);
        }
    }

    private void ResetScales()
    {
        foreach (var cell in Cells()) SetScale(cell, 1, animate: true);
    }

    private static void SetScale(FrameworkElement cell, double scale, bool animate)
    {
        if (cell.LayoutTransform is not ScaleTransform transform) return;
        if (animate)
        {
            var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(140)) { EasingFunction = Ease };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            return;
        }
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transform.ScaleX = transform.ScaleY = scale;
    }

    /// <summary>Cellules des jeux dans l'ordre, puis celle d'OptiGame.</summary>
    private IEnumerable<FrameworkElement> Cells()
    {
        for (var i = 0; i < ItemsHost.Items.Count; i++)
        {
            if (ItemsHost.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter presenter &&
                VisualTreeHelper.GetChildrenCount(presenter) > 0 &&
                VisualTreeHelper.GetChild(presenter, 0) is FrameworkElement cell)
            {
                yield return cell;
            }
        }
        yield return OgCell;
    }

    // ---- Clic, menu, glisser-déposer ----

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedItem = (sender as FrameworkElement)?.DataContext as DockItemViewModel;
        _pressPoint = e.GetPosition(Root);
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(Root) - _pressPoint;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8) return;

        var item = _pressedItem;
        _pressedItem = null;
        _dragging = true;
        ResetScales();
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, item.Id), DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            if (_autoHide && !Shelf.IsMouseOver) _hideTimer.Start();
        }
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as DockItemViewModel;
        if (item is not null && item == _pressedItem)
        {
            _vm.LaunchCommand.Execute(item);
        }
        _pressedItem = null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not Guid id) return;
        // Position d'arrivée : la cellule (au repos) la plus proche du point de dépôt.
        var position = e.GetPosition(ItemsHost);
        var axis = Horizontal ? position.X : position.Y;
        var index = (int)Math.Floor(axis / (IconSize + CellMargin));
        _vm.Move(id, Math.Clamp(index, 0, _vm.Items.Count - 1));
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
