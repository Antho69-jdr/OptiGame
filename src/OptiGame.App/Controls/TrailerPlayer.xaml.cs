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
/// ni menu, ni outils de développement, ni plein écran ; seule l'adresse d'une vidéo de Steam (*.steamstatic.com) est lue.
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

    /// <summary>Arrête la lecture et détruit le moteur web (ses processus se ferment avec lui).</summary>
    public void Stop()
    {
        _generation++;
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
    /// Page de lecture : la vidéo seule, contrôles natifs sans plein écran ni téléchargement, volume à moitié. Politique de
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
            </head><body><video id="v" src="{{src}}" controls autoplay playsinline disablepictureinpicture controlslist="nofullscreen nodownload noremoteplayback"></video>
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
