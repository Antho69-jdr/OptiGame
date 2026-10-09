using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Call;
using OptiGame.App.Controls;
using OptiGame.Core;
using OptiGame.Core.Call;
using OptiGame.Core.Logging;
using OptiGame.Core.Settings;
using OptiGame.Core.Text;

namespace OptiGame.App.ViewModels;

/// <summary>Étape d'un appel vocal.</summary>
public enum CallPhase
{
    /// <summary>Pas d'appel : démarrer ou rejoindre.</summary>
    Idle,
    /// <summary>Hôte : micro ouvert, recherche des adresses, invitation en préparation.</summary>
    Preparing,
    /// <summary>Hôte : invitation à envoyer, réponse de l'ami attendue.</summary>
    InvitationReady,
    /// <summary>Invité : réponse en préparation.</summary>
    Joining,
    /// <summary>Invité : réponse à renvoyer, l'appel commence quand l'hôte l'a collée.</summary>
    AnswerReady,
    /// <summary>Hôte : réponse collée, connexion en cours.</summary>
    Connecting,
    Connected,
}

/// <summary>
/// Page « Appel » : appel vocal pair à pair avec UN ami, sans serveur d'OptiGame ni compte. L'hôte crée une invitation (code à
/// copier), l'ami la colle et renvoie une réponse, l'hôte la colle : la voix passe ensuite directement, chiffrée, entre les deux
/// PC. Micro COUPÉ au départ ; mots de contrôle à comparer de vive voix. Pas de texte échangé. L'appel continue fenêtre fermée
/// (parties) : le moteur (<see cref="CallEngine"/>) n'appartient pas à la fenêtre. Singleton.
/// </summary>
public sealed partial class CallViewModel : ObservableObject, IDisposable
{
    /// <summary>Hôte : la connexion doit s'établir dans ce délai une fois la réponse collée.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly AppPaths _paths;
    private readonly AppSettingsStore _settings;
    private readonly FileLog _log;
    private CallEngine? _engine;
    private CancellationTokenSource? _timeout;
    private string? _offerSdp;
    private DateTime _connectedAt;
    private long _received;
    private long _sent;
    private string _path = "";
    private int _generation;

    public CallViewModel(AppPaths paths, AppSettingsStore settings, FileLog log)
    {
        _paths = paths;
        _settings = settings;
        _log = log;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsWorking), nameof(HasOwnCode), nameof(IsWaitingForAnswer), nameof(IsInCall), nameof(IsActive),
        nameof(OwnCodeTitle), nameof(OwnCodeHelp), nameof(WorkingText), nameof(ShowsSafetyWords))]
    [NotifyCanExecuteChangedFor(nameof(StartCallCommand), nameof(JoinCallCommand), nameof(AcceptAnswerCommand), nameof(HangUpCommand), nameof(ToggleMuteCommand), nameof(CopyCodeCommand))]
    private CallPhase _phase;

    public bool IsIdle => Phase == CallPhase.Idle;

    public bool IsWorking => Phase is CallPhase.Preparing or CallPhase.Joining or CallPhase.Connecting;

    public bool HasOwnCode => Phase is CallPhase.InvitationReady or CallPhase.AnswerReady;

    /// <summary>Hôte, invitation prête : champ « réponse de votre ami ».</summary>
    public bool IsWaitingForAnswer => Phase == CallPhase.InvitationReady;

    public bool IsInCall => Phase == CallPhase.Connected;

    /// <summary>Un appel est engagé (de la préparation au raccroché) : « Raccrocher » / « Annuler », micro ouvert par le moteur.</summary>
    public bool IsActive => Phase != CallPhase.Idle;

    public bool ShowsSafetyWords => SafetyWords.Length > 0 && Phase is CallPhase.AnswerReady or CallPhase.Connecting or CallPhase.Connected;

    public string OwnCodeTitle => Phase == CallPhase.AnswerReady ? "Votre réponse" : "Votre invitation";

    public string OwnCodeHelp => Phase == CallPhase.AnswerReady
        ? "Envoyez ce code à votre ami (message privé). L'appel commence dès qu'il l'a collé. Valable 15 minutes."
        : "Envoyez ce code à votre ami (message privé), puis collez ci-dessous la réponse qu'il vous renverra. Valable 15 minutes.";

    public string WorkingText => Phase switch
    {
        CallPhase.Preparing => "Préparation de l'invitation…",
        CallPhase.Joining => "Préparation de votre réponse…",
        CallPhase.Connecting => "Connexion à votre ami…",
        _ => "",
    };

    /// <summary>Code à envoyer à l'ami (invitation ou réponse) ; contient l'adresse réseau de ce PC.</summary>
    [ObservableProperty]
    private string _ownCode = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCallCommand))]
    private string _pastedInvitation = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptAnswerCommand))]
    private string _pastedAnswer = "";

    /// <summary>Message de la page (erreur, fin d'appel, code copié…) ; vide = aucun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    [ObservableProperty]
    private Severity _statusSeverity = Severity.Info;

    public bool HasStatus => Status.Length > 0;

    /// <summary>Windows refuse le micro aux applications de bureau : bouton vers ses paramètres de confidentialité.</summary>
    [ObservableProperty]
    private bool _canOpenMicrophoneSettings;

    /// <summary>4 mots tirés des empreintes de chiffrement des deux PC : identiques chez l'ami = connexion directe entre vous.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSafetyWords))]
    private string _safetyWords = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteLabel), nameof(MuteGlyph), nameof(MicrophoneState))]
    private bool _isMuted = true;

    public string MuteLabel => IsMuted ? "Ouvrir le micro" : "Couper le micro";

    // Segoe Fluent Icons : « Microphone » E720 pour l'ouvrir, « MicOff » EC54 pour le couper.
    public string MuteGlyph => IsMuted ? "" : "";

    public string MicrophoneState => IsMuted ? "Votre micro est coupé" : "Votre micro est ouvert";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeerState))]
    private bool _peerMuted;

    public string PeerState => PeerMuted ? "Le micro de votre ami est coupé" : "Le micro de votre ami est ouvert";

    /// <summary>Volume de l'ami, 0 à 100 (ce PC seulement).</summary>
    [ObservableProperty]
    private double _volume = 100;

    /// <summary>Niveau de votre voix (0 à 1), 0 micro coupé.</summary>
    [ObservableProperty]
    private double _localLevel;

    /// <summary>Niveau de la voix de l'ami (0 à 1).</summary>
    [ObservableProperty]
    private double _remoteLevel;

    [ObservableProperty]
    private string _duration = "";

    /// <summary>Nom du micro utilisé (celui de communication par défaut de Windows).</summary>
    [ObservableProperty]
    private string _microphone = "";

    /// <summary>« directe, même réseau » / « directe, par Internet ».</summary>
    [ObservableProperty]
    private string _connectionPath = "";

    /// <summary>Serveur public de découverte d'adresse (consentement, réglage gardé) : nécessaire hors du réseau local.</summary>
    public bool UseStun
    {
        get => _settings.Get().CallUseStun;
        set
        {
            if (value == UseStun) return;
            _settings.Update(s => s.CallUseStun = value);
            OnPropertyChanged();
        }
    }

    /// <summary>Octets audio reçus depuis le début de l'appel (vérifications).</summary>
    internal long BytesReceived => _received;

    internal string PathForTests => _path;

    /// <summary>Hôte : micro, adresses, puis invitation à copier.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StartCallAsync()
    {
        Begin(CallPhase.Preparing);
        var generation = _generation;
        if (await StartEngineAsync(generation) is not { } engine) return;
        engine.Send(new { cmd = "host", stun = UseStun });
    }

    private bool CanJoin() => IsIdle && PastedInvitation.Trim().Length > 0;

    /// <summary>Invité : invitation collée → réponse à renvoyer.</summary>
    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinCallAsync()
    {
        string offer;
        try
        {
            offer = InviteCode.Read(PastedInvitation, InviteKind.Invitation, DateTimeOffset.Now);
        }
        catch (FormatException ex)
        {
            ShowStatus(ex.Message, Severity.Warning);
            return;
        }
        Begin(CallPhase.Joining);
        _offerSdp = offer;
        var generation = _generation;
        if (await StartEngineAsync(generation) is not { } engine) return;
        engine.Send(new { cmd = "join", sdp = offer, stun = UseStun });
    }

    private bool CanAcceptAnswer() => Phase == CallPhase.InvitationReady && PastedAnswer.Trim().Length > 0;

    /// <summary>Hôte : réponse de l'ami collée → connexion.</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptAnswer))]
    private void AcceptAnswer()
    {
        string answer;
        try
        {
            answer = InviteCode.Read(PastedAnswer, InviteKind.Answer, DateTimeOffset.Now);
        }
        catch (FormatException ex)
        {
            ShowStatus(ex.Message, Severity.Warning);
            return;
        }
        SafetyWords = Core.Call.SafetyWords.For(InviteCode.Fingerprint(_offerSdp ?? ""), InviteCode.Fingerprint(answer));
        ClearStatus();
        Phase = CallPhase.Connecting;
        _engine?.Send(new { cmd = "answer", sdp = answer });
        ArmTimeout(ConnectTimeout);
    }

    [RelayCommand(CanExecute = nameof(HasOwnCode))]
    private void CopyCode()
    {
        try
        {
            System.Windows.Clipboard.SetText(OwnCode);
            ShowStatus("Code copié : collez-le dans un message privé à votre ami.", Severity.Success);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _log.Warn($"Presse-papiers occupé : {ex.Message}");
            ShowStatus("Le presse-papiers est occupé par un autre programme : réessayez dans un instant.", Severity.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _engine?.Send(new { cmd = "mute", value = IsMuted });
    }

    /// <summary>Raccrocher (ou annuler un appel en préparation) : l'ami est prévenu, le micro et le moteur sont libérés.</summary>
    [RelayCommand(CanExecute = nameof(IsActive))]
    private void HangUp()
    {
        var wasConnected = Phase == CallPhase.Connected;
        End(wasConnected ? "Appel terminé." : "", Severity.Info, tellPeer: true);
    }

    [RelayCommand]
    private void OpenMicrophoneSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Warn($"Paramètres du micro impossibles à ouvrir : {ex.Message}");
        }
    }

    partial void OnVolumeChanged(double value) => _engine?.Send(new { cmd = "volume", value = Math.Clamp(value, 0, 100) / 100 });

    private void Begin(CallPhase phase)
    {
        _generation++;
        ClearStatus();
        OwnCode = "";
        SafetyWords = "";
        PastedAnswer = "";
        IsMuted = true;
        PeerMuted = false;
        LocalLevel = RemoteLevel = 0;
        Duration = "";
        Microphone = "";
        ConnectionPath = "";
        _received = _sent = 0;
        _path = "";
        _offerSdp = null;
        Phase = phase;
    }

    private async Task<CallEngine?> StartEngineAsync(int generation)
    {
        try
        {
            var engine = await CallEngine.StartAsync(Path.Combine(_paths.Root, "webview-call"), _log);
            if (generation != _generation)
            {
                engine.Dispose(); // annulé pendant le démarrage
                return null;
            }
            _engine = engine;
            engine.Message += m => OnPageMessage(m, generation);
            if (Volume < 100) engine.Send(new { cmd = "volume", value = Volume / 100 });
            return engine;
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException ex)
        {
            _log.Warn($"Appel : moteur WebView2 absent ({ex.Message}).");
            if (generation == _generation) End("Le composant web de Windows (WebView2) est absent de ce PC : l'appel est impossible.", Severity.Error, tellPeer: false);
        }
        catch (Exception ex) when (ex is TimeoutException or FileNotFoundException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            _log.Error("Appel : moteur impossible à démarrer", ex);
            if (generation == _generation) End("L'appel n'a pas pu démarrer (détails dans le journal).", Severity.Error, tellPeer: false);
        }
        return null;
    }

    private void OnPageMessage(JsonElement message, int generation)
    {
        if (generation != _generation || !message.TryGetProperty("ev", out var evProperty)) return;
        switch (evProperty.GetString())
        {
            case "mic":
                Microphone = Text(message, "label");
                break;
            case "offer" when Phase == CallPhase.Preparing:
                _offerSdp = Text(message, "sdp");
                OwnCode = InviteCode.Create(InviteKind.Invitation, _offerSdp, DateTimeOffset.Now);
                _log.Info($"Appel : invitation prête ({Candidates(_offerSdp)}, {OwnCode.Length} caractères).");
                Phase = CallPhase.InvitationReady;
                ArmTimeout(InviteCode.Lifetime);
                break;
            case "answer" when Phase == CallPhase.Joining:
                var answer = Text(message, "sdp");
                OwnCode = InviteCode.Create(InviteKind.Answer, answer, DateTimeOffset.Now);
                SafetyWords = Core.Call.SafetyWords.For(InviteCode.Fingerprint(_offerSdp ?? ""), InviteCode.Fingerprint(answer));
                _log.Info($"Appel : réponse prête ({Candidates(answer)}, {OwnCode.Length} caractères).");
                Phase = CallPhase.AnswerReady;
                ArmTimeout(InviteCode.Lifetime);
                break;
            case "state":
                OnConnectionState(Text(message, "value"));
                break;
            case "muted":
                IsMuted = message.TryGetProperty("value", out var muted) && muted.ValueKind == JsonValueKind.True;
                break;
            case "peerMuted":
                PeerMuted = message.TryGetProperty("value", out var peerMuted) && peerMuted.ValueKind == JsonValueKind.True;
                break;
            case "levels":
                OnLevels(message);
                break;
            case "bye":
                _log.Info("Appel : l'ami a raccroché.");
                End("Votre ami a raccroché.", Severity.Info, tellPeer: false);
                break;
            case "error":
                OnPageError(Text(message, "name"), Text(message, "message"));
                break;
        }
    }

    private void OnConnectionState(string state)
    {
        switch (state)
        {
            case "connected" when Phase != CallPhase.Connected:
                _timeout?.Cancel();
                _connectedAt = DateTime.Now;
                ClearStatus();
                Phase = CallPhase.Connected;
                _log.Info("Appel : connecté.");
                break;
            case "connected":
                ClearStatus(); // reconnecté après une coupure
                break;
            case "disconnected" when Phase == CallPhase.Connected:
                ShowStatus("Connexion interrompue : nouvelle tentative…", Severity.Warning);
                break;
            case "failed":
                _log.Warn($"Appel : connexion impossible ou perdue (étape {Phase}, découverte d'adresse {(UseStun ? "activée" : "désactivée")}).");
                End(Phase == CallPhase.Connected ? "La connexion avec votre ami a été perdue." : FailureMessage(), Severity.Error, tellPeer: false);
                break;
        }
    }

    private string FailureMessage() => UseStun
        ? "Connexion impossible : vos box (routeurs) bloquent probablement les connexions directes. Essayez depuis un autre réseau, " +
          "par exemple le partage de connexion d'un téléphone."
        : "Connexion impossible. Si votre ami n'est pas sur le même réseau que vous, cochez « Passer par Internet » des deux côtés, " +
          "puis recommencez.";

    private void OnLevels(JsonElement message)
    {
        if (Phase != CallPhase.Connected) return;
        LocalLevel = Number(message, "local");
        RemoteLevel = Number(message, "remote");
        _received = (long)Number(message, "received");
        _sent = (long)Number(message, "sent");
        var path = Text(message, "path");
        if (path.Length > 0 && path != _path)
        {
            _path = path;
            ConnectionPath = path == "host/host" ? "directe, même réseau" : "directe, par Internet";
            _log.Info($"Appel : chemin {path}.");
        }
        var elapsed = DateTime.Now - _connectedAt;
        Duration = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
    }

    private void OnPageError(string name, string detail)
    {
        _log.Warn($"Appel : erreur de la page ({name} : {detail}).");
        switch (name)
        {
            case "NotAllowedError":
                End("Windows bloque le micro pour les applications de bureau. Autorisez-le dans Paramètres › Confidentialité › Microphone, puis recommencez.",
                    Severity.Error, tellPeer: false);
                CanOpenMicrophoneSettings = true;
                break;
            case "NotFoundError":
                End("Aucun micro n'est branché ou activé sur ce PC.", Severity.Error, tellPeer: false);
                break;
            case "NotReadableError":
                End("Le micro est inaccessible (utilisé par un autre programme en mode exclusif, ou désactivé).", Severity.Error, tellPeer: false);
                break;
            case "InvalidAccessError" or "OperationError" or "InvalidStateError":
                End("Ce code ne correspond pas à cet appel (une ancienne invitation ?). Recommencez depuis le début.", Severity.Error, tellPeer: false);
                break;
            default:
                End("L'appel s'est arrêté sur une erreur (détails dans le journal).", Severity.Error, tellPeer: false);
                break;
        }
    }

    private void ArmTimeout(TimeSpan delay)
    {
        _timeout?.Cancel();
        _timeout = new CancellationTokenSource();
        var token = _timeout.Token;
        var generation = _generation;
        var phase = Phase;
        _ = Task.Delay(delay, token).ContinueWith(_ =>
        {
            if (generation != _generation || Phase != phase) return;
            _log.Warn($"Appel : délai dépassé (étape {phase}).");
            End(phase == CallPhase.Connecting ? FailureMessage() : "Le code a expiré (15 minutes) : recommencez quand votre ami est prêt.",
                phase == CallPhase.Connecting ? Severity.Error : Severity.Info, tellPeer: false);
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Fin de l'appel : moteur détruit (micro libéré), page remise au départ, message gardé.</summary>
    private void End(string status, Severity severity, bool tellPeer)
    {
        _timeout?.Cancel();
        _generation++;
        if (Phase == CallPhase.Connected)
        {
            _log.Info($"Appel terminé après {Duration} ({FrenchText.Count((int)(_received / 1024), "Ko reçu", "Ko reçus")}, {_sent / 1024} Ko envoyés).");
        }
        var engine = _engine;
        _engine = null;
        if (engine is not null)
        {
            if (tellPeer)
            {
                engine.Send(new { cmd = "hangup" });
                // Le « raccroché » part avant la destruction du moteur.
                _ = Task.Delay(600).ContinueWith(_ => engine.Dispose(), TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                engine.Dispose();
            }
        }
        CanOpenMicrophoneSettings = false;
        OwnCode = "";
        PastedInvitation = "";
        PastedAnswer = "";
        SafetyWords = "";
        IsMuted = true;
        PeerMuted = false;
        LocalLevel = RemoteLevel = 0;
        Phase = CallPhase.Idle;
        if (status.Length > 0) ShowStatus(status, severity);
        else ClearStatus();
    }

    private void ShowStatus(string text, Severity severity)
    {
        StatusSeverity = severity;
        Status = text;
    }

    private void ClearStatus() => Status = "";

    private static string Text(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static double Number(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    /// <summary>« 3 adresses (2 locales, 1 Internet) » : pour le journal, sans les adresses elles-mêmes.</summary>
    private static string Candidates(string sdp)
    {
        var lines = sdp.Split('\n').Where(l => l.StartsWith("a=candidate:", StringComparison.Ordinal)).ToList();
        var host = lines.Count(l => l.Contains(" typ host", StringComparison.Ordinal));
        var reflexive = lines.Count(l => l.Contains(" typ srflx", StringComparison.Ordinal));
        return $"{FrenchText.Count(lines.Count, "adresse", "adresses")} : {host} locale(s), {reflexive} Internet";
    }

    /// <summary>Fermeture d'OptiGame : l'ami est prévenu si possible, le micro libéré.</summary>
    public void Dispose()
    {
        if (_engine is { } engine)
        {
            engine.Send(new { cmd = "hangup" });
            engine.Dispose();
            _engine = null;
        }
    }
}
