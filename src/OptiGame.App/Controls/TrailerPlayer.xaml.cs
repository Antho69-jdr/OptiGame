using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;

namespace OptiGame.App.Controls;

/// <summary>
/// Bande-annonce du magasin Steam (flux HLS, lu nativement par le moteur web de Windows, WebView2 : vérifié le 2026-10-07).
/// Léger par construction : seule la vignette existe tant qu'on ne clique pas sur « Lire » ; le moteur web (plusieurs
/// processus) est créé au clic et DÉTRUIT dès que la vidéo n'est plus visible (fiche quittée, autre onglet, fenêtre fermée)
/// ou qu'une partie commence (<see cref="CanPlay"/>). Page verrouillée : une seule navigation (la nôtre), ni nouvelle fenêtre,
/// ni menu, ni outils de développement ; plein écran = la vidéo passe dans une fenêtre sans bord (<see cref="SetFullScreen"/>) ; seule l'adresse d'une vidéo de Steam (*.steamstatic.com) est lue.
/// </summary>
public partial class TrailerPlayer : UserControl
{
    /// <summary>Largeur maximale de la vidéo (16:9).</summary>
    private const double MaxVideoWidth = 640;

    private static FileLog? _log;
    private static string? _dataFolder;

    /// <summary>Journal et dossier du moteur web (%LocalAppData%\OptiGame\webview : Program Files n'est pas modifiable).</summary>
    public static void Attach(FileLog log, string dataFolder)
    {
        _log = log;
        _dataFolder = dataFolder;
    }

    public static readonly DependencyProperty ThumbnailProperty = DependencyProperty.Register(
        nameof(Thumbnail), typeof(ImageSource), typeof(TrailerPlayer), new PropertyMetadata(null, (d, _) => ((TrailerPlayer)d).UpdatePoster()));

    public static readonly DependencyProperty VideoUrlProperty = DependencyProperty.Register(
        nameof(VideoUrl), typeof(string), typeof(TrailerPlayer), new PropertyMetadata(null, (d, _) => ((TrailerPlayer)d).Stop()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TrailerPlayer), new PropertyMetadata("", (d, _) => ((TrailerPlayer)d).UpdatePoster()));

    public static readonly DependencyProperty CanPlayProperty = DependencyProperty.Register(
        nameof(CanPlay), typeof(bool), typeof(TrailerPlayer), new PropertyMetadata(true, (d, _) => ((TrailerPlayer)d).OnCanPlayChanged()));

    public ImageSource? Thumbnail
    {
        get => (ImageSource?)GetValue(ThumbnailProperty);
        set => SetValue(ThumbnailProperty, value);
    }

    /// <summary>Flux HLS de la bande-annonce ; le changer arrête la lecture en cours.</summary>
    public string? VideoUrl
    {
        get => (string?)GetValue(VideoUrlProperty);
        set => SetValue(VideoUrlProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Faux pendant une partie : rien n'est lu, et une lecture en cours s'arrête.</summary>
    public bool CanPlay
    {
        get => (bool)GetValue(CanPlayProperty);
        set => SetValue(CanPlayProperty, value);
    }

    private WebView2? _web;

    /// <summary>Numéro de la lecture en cours : une création du moteur terminée après un arrêt est abandonnée.</summary>
    private int _generation;

    public TrailerPlayer()
    {
        InitializeComponent();
        Root.SizeChanged += (_, _) => Fit();
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) Stop(); };
        Unloaded += (_, _) => Stop();
        ToolTipService.SetShowOnDisabled(Poster, true); // pendant une partie : dire pourquoi la vidéo attend
        UpdatePoster();
    }

    private void Fit()
    {
        var width = Math.Min(Root.ActualWidth, MaxVideoWidth);
        if (width <= 0) return;
        Frame.Width = width;
        Frame.Height = Math.Round(width * 9 / 16);
    }

    private void UpdatePoster()
    {
        Poster.Background = Thumbnail is { } image
            ? new ImageBrush(image) { Stretch = Stretch.UniformToFill }
            : (Brush)FindResource("Brush.BannerPlaceholder");
        var title = string.IsNullOrWhiteSpace(Title) ? "" : $" : {Title}";
        AutomationProperties.SetName(Poster, $"Lire la bande-annonce{title}");
        Poster.ToolTip = CanPlay ? $"Lire la bande-annonce{title}" : "Bande-annonce disponible après la partie en cours.";
        Poster.IsEnabled = CanPlay;
    }

    private void OnCanPlayChanged()
    {
        if (!CanPlay) Stop();
        UpdatePoster();
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => _ = PlayAsync();

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Stop();
        Poster.Focus();
    }

    /// <summary>Lecture (clic sur la vignette ; aussi appelée par le mode capture pour mesurer le coût réel du lecteur).</summary>
    internal async Task PlayAsync()
    {
        if (!CanPlay || VideoUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !SteamStoreAbout.IsSteamMedia(uri)) return;
        Stop();
        var generation = ++_generation;
        ShowStatus("Chargement de la vidéo…");
        try
        {
            // Lecture automatique avec le son : c'est l'utilisateur qui vient de cliquer sur « Lire ». Cache disque borné à 10 Mo
            // (les morceaux de vidéo n'ont pas à s'accumuler dans webview).
            var environment = await CoreWebView2Environment.CreateAsync(null, _dataFolder,
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disk-cache-size=10485760"));
            if (generation != _generation || !IsVisible || !CanPlay) return;

            var web = new WebView2 { DefaultBackgroundColor = ToDrawing((Color)FindResource("Color.Window")) };
            _web = web;
            Host.Child = web;
            _clipKey = null;
            LayoutUpdated += OnLayoutUpdated; // découpage recalculé à chaque mise en page (défilement compris)
            Host.Visibility = Visibility.Visible;
            Poster.Visibility = Visibility.Hidden;
            CloseButton.Visibility = Visibility.Visible;
            await web.EnsureCoreWebView2Async(environment);
            if (generation != _generation) return; // arrêtée entre-temps : Stop a déjà détruit le moteur

            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsReputationCheckingRequired = false; // page locale : aucune adresse envoyée au filtre de réputation
            core.NewWindowRequested += (_, a) => a.Handled = true;
            var navigated = false;
            core.NavigationStarting += (_, a) =>
            {
                if (navigated) a.Cancel = true; // une seule page : la nôtre
                navigated = true;
            };
            core.WebMessageReceived += (_, a) => OnPageMessage(a.TryGetWebMessageAsString(), generation, environment);
            // Bouton plein écran du lecteur, double-clic, Échap : la page le demande, la vidéo change de fenêtre sans s'arrêter.
            core.ContainsFullScreenElementChanged += (_, _) => { if (generation == _generation) SetFullScreen(core.ContainsFullScreenElement); };
            core.NavigateToString(Page(uri, (Color)FindResource("Color.Window")));
            ShowStatus("");
        }
        catch (Exception ex) when (generation != _generation && ex is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // lecture arrêtée pendant la création du moteur : rien à signaler
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            Stop();
            ShowStatus("Le lecteur vidéo de Windows (WebView2) est absent de ce PC : regardez la bande-annonce sur Steam.");
            _log?.Warn($"Bande-annonce : moteur WebView2 absent ({ex.Message}).");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            Stop();
            ShowStatus("La vidéo n'a pas pu être lue. Regardez la bande-annonce sur Steam.");
            _log?.Error("Bande-annonce : moteur web impossible à démarrer", ex);
        }
    }

    private void OnPageMessage(string? message, int generation, CoreWebView2Environment environment)
    {
        if (generation != _generation) return;
        if (message == "playing")
        {
            _ = LogFootprintAsync(generation, environment);
        }
        else if (message?.StartsWith("error", StringComparison.Ordinal) == true)
        {
            Stop();
            ShowStatus("La vidéo n'a pas pu être lue. Regardez la bande-annonce sur Steam.");
            _log?.Warn($"Bande-annonce illisible ({message}) : {VideoUrl}");
        }
    }

    /// <summary>Mémoire des processus du moteur web 4 s après le début de la lecture (journal : coût réel du lecteur).</summary>
    private async Task LogFootprintAsync(int generation, CoreWebView2Environment environment)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (generation != _generation || _log is null) return;
        try
        {
            var processes = environment.GetProcessInfos();
            long workingSet = 0, privateBytes = 0;
            foreach (var info in processes)
            {
                try
                {
                    using var process = Process.GetProcessById(info.ProcessId);
                    workingSet += process.WorkingSet64;
                    privateBytes += process.PrivateMemorySize64;
                }
                catch (ArgumentException)
                {
                    // processus terminé entre-temps
                }
            }
            _log.Info($"Bande-annonce en lecture : {processes.Count} processus du moteur web, {workingSet / 1048576} Mo en RAM, {privateBytes / 1048576} Mo privés.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _log.Warn($"Bande-annonce : mémoire du moteur web illisible ({ex.Message}).");
        }
    }

    // ---- Découpage de la vidéo (fenêtre hébergée : WPF ne peut rien dessiner dessus et ne la rogne pas) ----

    /// <summary>
    /// Élément posé au-dessus de la page (fil d'Ariane flottant) : la vidéo est découpée pour ne jamais le recouvrir.
    /// </summary>
    private static readonly List<WeakReference<FrameworkElement>> Overlays = [];

    public static readonly DependencyProperty IsAboveVideoProperty = DependencyProperty.RegisterAttached(
        "IsAboveVideo", typeof(bool), typeof(TrailerPlayer), new PropertyMetadata(false, (d, e) =>
        {
            if (d is FrameworkElement element && e.NewValue is true) Overlays.Add(new WeakReference<FrameworkElement>(element));
        }));

    public static bool GetIsAboveVideo(DependencyObject element) => (bool)element.GetValue(IsAboveVideoProperty);

    public static void SetIsAboveVideo(DependencyObject element, bool value) => element.SetValue(IsAboveVideoProperty, value);


    /// <summary>Dernier découpage appliqué (en pixels), pour ne rappeler Windows que s'il change.</summary>
    private string? _clipKey;

    /// <summary>
    /// Découpe la fenêtre de la vidéo (SetWindowRgn) : seulement la partie visible dans la zone qui défile, moins les éléments
    /// marqués <see cref="IsAboveVideoProperty"/>. Sans cela, la vidéo défilée déborderait sur le fil d'Ariane flottant.
    /// </summary>
    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateClip();

    private void UpdateClip()
    {
        if (_web is not { } web || _fullScreen is not null || web.Handle == IntPtr.Zero || !web.IsVisible) return;
        if (PresentationSource.FromVisual(web)?.CompositionTarget is not { } target) return;
        var scale = target.TransformToDevice;
        var bounds = new Rect(0, 0, web.ActualWidth, web.ActualHeight);
        var visible = bounds;
        if (FindAncestor<ScrollViewer>(this) is { } scroller)
        {
            visible.Intersect(scroller.TransformToVisual(web).TransformBounds(new Rect(0, 0, scroller.ViewportWidth, scroller.ViewportHeight)));
        }
        var holes = new List<Rect>();
        var window = Window.GetWindow(this);
        Overlays.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var reference in Overlays)
        {
            if (!reference.TryGetTarget(out var overlay) || !overlay.IsVisible || Window.GetWindow(overlay) != window) continue;
            var hole = overlay.TransformToVisual(web).TransformBounds(new Rect(overlay.RenderSize));
            hole.Intersect(bounds);
            if (!hole.IsEmpty) holes.Add(hole);
        }

        Int32Rect Device(Rect r) => r.IsEmpty ? default : new Int32Rect(
            (int)Math.Floor(r.Left * scale.M11), (int)Math.Floor(r.Top * scale.M22),
            (int)Math.Ceiling(r.Width * scale.M11), (int)Math.Ceiling(r.Height * scale.M22));
        var shown = Device(visible);
        var cut = holes.Select(Device).ToList();
        var key = $"{shown}|{string.Join(";", cut)}";
        if (key == _clipKey) return;
        _clipKey = key;

        var region = CreateRectRgn(shown.X, shown.Y, shown.X + shown.Width, shown.Y + shown.Height);
        foreach (var hole in cut)
        {
            var holeRegion = CreateRectRgn(hole.X, hole.Y, hole.X + hole.Width, hole.Y + hole.Height);
            CombineRgn(region, region, holeRegion, RgnDiff);
            DeleteObject(holeRegion);
        }
        if (SetWindowRgn(web.Handle, region, true) == 0) DeleteObject(region); // en cas de succès, Windows possède la région
    }

    /// <summary>Plus de découpage (plein écran) ; le prochain calcul repartira de zéro.</summary>
    private void ClearClip()
    {
        _clipKey = null;
        if (_web is { Handle: var handle } && handle != IntPtr.Zero) SetWindowRgn(handle, IntPtr.Zero, true);
    }

    /// <summary>Découpage actuel de la vidéo (mode capture : vérifier qu'elle ne déborde pas sur le fil d'Ariane).</summary>
    internal string ClipDescription()
    {
        if (_web is not { Handle: var handle } || handle == IntPtr.Zero) return "aucun lecteur";
        var kind = GetWindowRgnBox(handle, out var box);
        return kind switch
        {
            0 => "aucun découpage (vidéo entière)",
            1 => "vidéo entièrement masquée",
            _ => $"zone affichée {box.Right - box.Left}×{box.Bottom - box.Top} px à ({box.Left}, {box.Top}){(kind == 3 ? ", trouée (fil d'Ariane)" : "")}",
        };
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(start); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T found) return found;
        }
        return null;
    }

    private const int RgnDiff = 4;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowRgnBox(IntPtr window, out NativeRect box);

    /// <summary>Fenêtre plein écran pendant que la vidéo y est ; null sinon.</summary>
    private Window? _fullScreen;

    /// <summary>
    /// Plein écran : le moteur web (fenêtre hébergée) passe, SANS être recréé ni la vidéo relancée, dans une fenêtre sans bord
    /// agrandie sur l'écran d'OptiGame ; puis revient dans la fiche. <paramref name="offScreen"/> : essai du mode capture, fenêtre
    /// hors des écrans et jamais activée (rien ne doit apparaître devant l'utilisateur).
    /// </summary>
    internal void SetFullScreen(bool on, bool offScreen = false)
    {
        if (_web is not { } web) return;
        if (on && _fullScreen is null)
        {
            var owner = Window.GetWindow(this);
            var window = new Window
            {
                Title = string.IsNullOrWhiteSpace(Title) ? "Bande-annonce" : $"Bande-annonce : {Title}",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = !offScreen,
                Background = (Brush)FindResource("Brush.Window"),
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.Manual,
                // Sur l'écran de la fenêtre d'OptiGame (agrandie ensuite : sans bord, elle couvre aussi la barre des tâches).
                Left = offScreen ? -20000 : (owner?.Left ?? 0) + 40,
                Top = offScreen ? -20000 : (owner?.Top ?? 0) + 40,
                Width = 640,
                Height = 360,
            };
            // Fermée autrement (Alt+F4) : la vidéo revient dans la fiche AVANT la destruction de la fenêtre.
            window.Closing += (_, _) =>
            {
                if (_fullScreen != window) return;
                _fullScreen = null;
                window.Content = null;
                Host.Child = web;
                _clipKey = null;
                _ = web.CoreWebView2?.ExecuteScriptAsync("if (document.fullscreenElement) document.exitFullscreen();");
            };
            _fullScreen = window;
            Host.Child = null;
            window.Content = web;
            ClearClip(); // plein écran : vidéo entière
            window.Show();
            if (!offScreen)
            {
                window.WindowState = WindowState.Maximized;
                window.Activate();
                web.Focus();
            }
            _log?.Info("Bande-annonce en plein écran.");
        }
        else if (!on && _fullScreen is { } window)
        {
            _fullScreen = null;
            window.Content = null;
            Host.Child = web;
            _clipKey = null;
            window.Close();
            if (!offScreen) Window.GetWindow(this)?.Activate();
        }
    }

    /// <summary>Position et état de la vidéo (mode capture : vérifier que la lecture continue pendant le plein écran).</summary>
    internal async Task<string> ProbeAsync() =>
        _web?.CoreWebView2 is { } core
            ? await core.ExecuteScriptAsync("(() => { const v = document.getElementById('v'); return v.currentTime.toFixed(1) + (v.paused ? ' en pause' : ' en lecture'); })()")
            : "aucun lecteur";

    internal bool IsFullScreen => _fullScreen is not null;

    /// <summary>Arrête la lecture et détruit le moteur web (ses processus se ferment avec lui).</summary>
    public void Stop()
    {
        _generation++;
        if (_fullScreen is { } fullScreen)
        {
            _fullScreen = null;
            fullScreen.Content = null;
            fullScreen.Close();
        }
        LayoutUpdated -= OnLayoutUpdated;
        _clipKey = null;
        if (_web is { } web)
        {
            _web = null;
            Host.Child = null;
            web.Dispose();
            _log?.Info("Bande-annonce fermée : moteur web détruit.");
        }
        Host.Visibility = Visibility.Collapsed;
        Poster.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Collapsed;
        if (Status.Text == "Chargement de la vidéo…") ShowStatus("");
    }

    private void ShowStatus(string text)
    {
        Status.Text = text;
        Status.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Page de lecture : la vidéo seule, contrôles natifs (plein écran compris, sans téléchargement), volume à moitié. Politique de
    /// sécurité : médias de *.steamstatic.com seulement, aucun autre contenu chargé.
    /// </summary>
    private static string Page(Uri video, Color background)
    {
        var color = $"#{background.R:X2}{background.G:X2}{background.B:X2}";
        var src = WebUtility.HtmlEncode(video.AbsoluteUri);
        return $$"""
            <!doctype html><html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; media-src https://*.steamstatic.com blob:; connect-src https://*.steamstatic.com; script-src 'unsafe-inline'; style-src 'unsafe-inline'">
            <style>html,body{margin:0;height:100%;background:{{color}};overflow:hidden}video{width:100%;height:100%;background:{{color}}}</style>
            </head><body><video id="v" src="{{src}}" controls autoplay playsinline disablepictureinpicture controlslist="nodownload noremoteplayback"></video>
            <script>
            const v = document.getElementById('v');
            v.volume = 0.5;
            v.addEventListener('playing', () => chrome.webview.postMessage('playing'), { once: true });
            v.addEventListener('error', () => chrome.webview.postMessage('error ' + (v.error ? v.error.code : '?')));
            </script></body></html>
            """;
    }

    private static System.Drawing.Color ToDrawing(Color color) => System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
}
