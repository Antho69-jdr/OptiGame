using System.Text.Json;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using OptiGame.Core.Logging;

namespace OptiGame.App.Call;

/// <summary>
/// Moteur d'un appel vocal : la page Call/call.html (WebRTC) dans un moteur web WebView2 posé sur une fenêtre native jamais affichée
/// (ni Window WPF : elle pourrait devenir Application.MainWindow, ni barre des tâches). Créé au début d'un appel, détruit au
/// raccroché : aucun processus du moteur entre deux appels. Indépendant de la fenêtre principale, que les parties ferment.
/// Verrouillé : une seule page (la nôtre, servie depuis le dossier de l'appli), micro autorisé pour elle SEULE, tout le reste
/// refusé (caméra, notifications, nouvelles fenêtres, menus, outils).
/// </summary>
internal sealed class CallEngine : IDisposable
{
    /// <summary>Hôte virtuel de la page : https donne le contexte sécurisé exigé pour le micro (vérifié le 2026-10-08).</summary>
    public const string Host = "appel.optigame";

    private static readonly Uri Page = new($"https://{Host}/call.html");

    /// <summary>
    /// Micro et autorisations simulés par le moteur (son de test) : mode capture et vérifications seulement, jamais le vrai micro.
    /// </summary>
    public static bool UseFakeMedia { get; set; }

    /// <summary>Son lu par le micro simulé (fichier WAV) ; null = le bip du moteur.</summary>
    public static string? FakeAudioFile { get; set; }

    private readonly FileLog _log;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HwndSource? _window;
    private CoreWebView2Controller? _controller;

    private CallEngine(FileLog log) => _log = log;

    /// <summary>Message de la page (champ « ev » : ready, mic, offer, answer, state, muted, peerMuted, levels, bye, error).</summary>
    public event Action<JsonElement>? Message;

    /// <param name="dataFolder">Dossier du moteur (%LocalAppData%\OptiGame\webview-call ; options différentes de celles des bandes-annonces).</param>
    public static async Task<CallEngine> StartAsync(string dataFolder, FileLog log)
    {
        var engine = new CallEngine(log);
        try
        {
            await engine.InitializeAsync(dataFolder);
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private async Task InitializeAsync(string dataFolder)
    {
        var pageFolder = Path.Combine(AppContext.BaseDirectory, "Call");
        if (!File.Exists(Path.Combine(pageFolder, "call.html"))) throw new FileNotFoundException("Page d'appel absente de l'installation.", Path.Combine(pageFolder, "call.html"));

        // Fenêtre native cachée (WS_POPUP sans WS_VISIBLE, fenêtre outil, jamais activée) : support du moteur, rien à l'écran.
        _window = new HwndSource(new HwndSourceParameters("OptiGame — Appel")
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080 | 0x08000000,
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1,
        });

        var arguments = "--autoplay-policy=no-user-gesture-required";
        if (UseFakeMedia) arguments += " --use-fake-device-for-media-stream --use-fake-ui-for-media-stream";
        if (UseFakeMedia && FakeAudioFile is not null) arguments += $" --use-file-for-fake-audio-capture=\"{FakeAudioFile}\"";
        var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder, new CoreWebView2EnvironmentOptions(arguments));
        _controller = await environment.CreateCoreWebView2ControllerAsync(_window.Handle);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, 1, 1);
        _controller.IsVisible = true; // page « visible » : minuteries à leur rythme normal (indicateurs de voix)

        var core = _controller.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsReputationCheckingRequired = false; // page locale : aucune adresse envoyée au filtre de réputation
        core.SetVirtualHostNameToFolderMapping(Host, pageFolder, CoreWebView2HostResourceAccessKind.Deny);
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.NavigationStarting += (_, a) =>
        {
            if (!Uri.TryCreate(a.Uri, UriKind.Absolute, out var uri) || uri != Page) a.Cancel = true; // une seule page : la nôtre
        };
        core.PermissionRequested += (_, a) =>
        {
            var ours = Uri.TryCreate(a.Uri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == Host;
            a.State = ours && a.PermissionKind == CoreWebView2PermissionKind.Microphone ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            if (a.State == CoreWebView2PermissionState.Deny) _log.Warn($"Appel : autorisation refusée ({a.PermissionKind}, {a.Uri}).");
        };
        core.WebMessageReceived += (_, a) => OnPageMessage(a.TryGetWebMessageAsString());
        core.ProcessFailed += (_, a) =>
        {
            _log.Warn($"Appel : processus du moteur arrêté ({a.ProcessFailedKind}, {a.Reason}).");
            Raise($$"""{"ev":"error","name":"ProcessFailed","message":"{{a.ProcessFailedKind}}"}""");
        };

        core.Navigate(Page.AbsoluteUri);
        if (await Task.WhenAny(_ready.Task, Task.Delay(TimeSpan.FromSeconds(20))) != _ready.Task)
        {
            throw new TimeoutException("La page d'appel ne s'est pas chargée.");
        }
    }

    /// <summary>Commande à la page (« cmd » : host, join, answer, mute, volume, hangup).</summary>
    public void Send(object command)
    {
        _controller?.CoreWebView2.PostWebMessageAsString(JsonSerializer.Serialize(command));
    }

    private void OnPageMessage(string? text)
    {
        if (text is not null) Raise(text);
    }

    private void Raise(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var message = document.RootElement.Clone();
            if (message.TryGetProperty("ev", out var ev) && ev.GetString() == "ready") _ready.TrySetResult();
            Message?.Invoke(message);
        }
        catch (JsonException)
        {
            _log.Warn("Appel : message illisible de la page.");
        }
    }

    public void Dispose()
    {
        Message = null;
        _controller?.Close(); // dernier contrôleur de l'environnement : les processus du moteur s'arrêtent
        _controller = null;
        _window?.Dispose();
        _window = null;
    }
}
