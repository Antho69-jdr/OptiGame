using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Call;
using OptiGame.App.Controls;
using OptiGame.Core;
using OptiGame.Core.Call;
using OptiGame.Core.Logging;
using OptiGame.Core.Settings;
using OptiGame.Platform.Input;

namespace OptiGame.App.ViewModels;

/// <summary>Étape d'un appel vocal.</summary>
public enum CallPhase
{
    /// <summary>Pas d'appel : appeler ou rejoindre.</summary>
    Idle,
    /// <summary>Micro ouvert, salon en cours d'ouverture sur le serveur de mise en relation.</summary>
    Preparing,
    /// <summary>Hôte : code affiché, l'ami a 2 minutes pour le saisir.</summary>
    Waiting,
    /// <summary>Les deux PC sont sur le serveur et s'échangent leurs adresses.</summary>
    Connecting,
    Connected,
}

/// <summary>Micro ou sortie audio proposé ; Id vide = celui de Windows par défaut (suivi quand il change).</summary>
public sealed record CallDevice(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Page « Appel » : appel vocal avec UN ami, voix directe et chiffrée entre les deux PC. L'hôte obtient un code court
/// (« OG-K7P2Q9 », 2 minutes) ; l'ami le saisit ; le serveur de mise en relation (<see cref="CallRelay"/>) leur fait échanger leurs
/// adresses au même instant puis s'efface. Micro COUPÉ au départ ; mots de contrôle à comparer de vive voix ; micro et sortie au
/// choix (par défaut : ceux de Windows, suivis quand ils changent). Pas de texte. L'appel continue fenêtre fermée (parties) : le
/// moteur (<see cref="CallEngine"/>) n'appartient pas à la fenêtre. Singleton.
/// </summary>
public sealed partial class CallViewModel : ObservableObject, IDisposable
{
    /// <summary>Une fois l'ami arrivé, la connexion directe doit s'établir dans ce délai.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly AppPaths _paths;
    private readonly AppSettingsStore _settings;
    private readonly FileLog _log;
    private readonly DispatcherTimer _ticker;
    private CallEngine? _engine;
    private CancellationTokenSource? _timeout;
    private bool _isHost;
    private string _rawCode = "";
    private DateTimeOffset _expiresAt;
    private DateTime _connectedAt;
    private long _received;
    private long _sent;
    private string _path = "";
    private int _generation;
    private int _codeRetries;
    private bool _updatingDevices;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private RawKeyboardListener? _keyboard;
    private volatile MicHotkeyMatcher? _matcher;

    public CallViewModel(AppPaths paths, AppSettingsStore settings, FileLog log, SteamFriendsService friends, Services.INotificationService notifications)
    {
        _paths = paths;
        _settings = settings;
        _log = log;
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => UpdateCountdown();
        Friends = friends;
        _notifications = notifications;
        friends.Ring += OnRing;
        friends.Declined += OnDeclined;
        friends.Cancelled += OnCancelled;
        friends.CallError += OnCallError;
        friends.FriendRequest += OnFriendRequest;
        friends.FriendAccepted += OnFriendAccepted;
        friends.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SteamFriendsService.Contacts)) RefreshContacts(); };
        RefreshContacts();
    }

    /// <summary>Serveur de mise en relation (remplacé par la variable OPTIGAME_CALL_RELAY pour les essais) ; null = pas en place.</summary>
    internal Uri? Relay { get; set; } = CallRelay.Resolve(Environment.GetEnvironmentVariable(CallRelay.OverrideVariable));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsWorking), nameof(IsWaiting), nameof(IsInCall), nameof(IsActive), nameof(WorkingText),
        nameof(ShowsSafetyWords), nameof(ShowsMeter))]
    [NotifyCanExecuteChangedFor(nameof(StartCallCommand), nameof(JoinCallCommand), nameof(HangUpCommand), nameof(ToggleMuteCommand), nameof(CopyCodeCommand))]
    private CallPhase _phase;

    public bool IsIdle => Phase == CallPhase.Idle;

    public bool IsWorking => Phase is CallPhase.Preparing or CallPhase.Connecting;

    /// <summary>Hôte : code à donner, compte à rebours.</summary>
    public bool IsWaiting => Phase == CallPhase.Waiting;

    public bool IsInCall => Phase == CallPhase.Connected;

    /// <summary>Un appel est engagé (de la préparation au raccroché) : micro ouvert par le moteur, « Annuler » / « Raccrocher ».</summary>
    public bool IsActive => Phase != CallPhase.Idle;

    public bool ShowsSafetyWords => SafetyWords.Length > 0 && Phase is CallPhase.Connecting or CallPhase.Connected;

    public string WorkingText => Phase switch
    {
        CallPhase.Preparing when _isHost => "Création du code…",
        CallPhase.Preparing => "Recherche de l'appel de votre ami…",
        CallPhase.Connecting => "Connexion à votre ami…",
        _ => "",
    };

    /// <summary>Code à donner à l'ami (hôte) : « OG-K7P2Q9 ».</summary>
    [ObservableProperty]
    private string _code = "";

    /// <summary>« Valable encore 1:43 ».</summary>
    [ObservableProperty]
    private string _countdown = "";

    /// <summary>Code saisi par l'invité.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCallCommand))]
    private string _joinCode = "";

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

    [ObservableProperty]
    private double _localLevel;

    [ObservableProperty]
    private double _remoteLevel;

    [ObservableProperty]
    private string _duration = "";

    /// <summary>« directe, même réseau » / « directe, par Internet ».</summary>
    [ObservableProperty]
    private string _connectionPath = "";

    /// <summary>Micros proposés (le premier = celui de Windows par défaut), connus une fois le micro ouvert.</summary>
    public ObservableCollection<CallDevice> Microphones { get; } = [];

    public ObservableCollection<CallDevice> Speakers { get; } = [];

    /// <summary>Choix de la suppression du bruit (gardé ; appliqué tout de suite pendant un appel).</summary>
    public IReadOnlyList<NoiseOption> NoiseOptions { get; } =
    [
        new(NoiseSuppression.Strong, "Forte (recommandée)"),
        new(NoiseSuppression.Standard, "Standard"),
        new(NoiseSuppression.Off, "Aucune"),
    ];

    public NoiseOption SelectedNoise
    {
        get => NoiseOptions.First(o => o.Mode == _settings.Get().CallNoiseSuppression);
        set
        {
            if (value is null || value.Mode == _settings.Get().CallNoiseSuppression) return;
            _settings.Update(s => s.CallNoiseSuppression = value.Mode);
            _engine?.Send(new { cmd = "noise", mode = NoiseMode(value.Mode) });
            _testEngine?.Send(new { cmd = "noise", mode = NoiseMode(value.Mode) });
            _log.Info($"Appel : suppression du bruit « {value.Label} ».");
            OnPropertyChanged();
        }
    }

    private static string NoiseMode(NoiseSuppression mode) => mode switch
    {
        NoiseSuppression.Strong => "strong",
        NoiseSuppression.Standard => "standard",
        _ => "off",
    };

    [ObservableProperty]
    private CallDevice? _selectedMicrophone;

    [ObservableProperty]
    private CallDevice? _selectedSpeaker;

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

    /// <summary>Trames traitées par le filtre de bruit et probabilité de voix de la dernière seconde (vérifications).</summary>
    internal long NoiseFrames { get; private set; }

    internal double NoiseVoice { get; private set; }

    /// <summary>Hôte : micro, salon sur le serveur, puis code à donner.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task StartCallAsync()
    {
        _codeRetries = 0;
        _outgoing = null;
        NotifyWaitingTexts();
        return OpenAsync(host: true, CallCode.Create());
    }

    private bool CanJoin() => IsIdle && JoinCode.Trim().Length > 0;

    /// <summary>Invité : code saisi → connexion.</summary>
    [RelayCommand(CanExecute = nameof(CanJoin))]
    private Task JoinCallAsync()
    {
        string code;
        try
        {
            code = CallCode.Parse(JoinCode);
        }
        catch (FormatException ex)
        {
            ShowStatus(ex.Message, Severity.Warning);
            return Task.CompletedTask;
        }
        return OpenAsync(host: false, code);
    }

    private async Task OpenAsync(bool host, string code)
    {
        if (IsTestingMic) StopMicTest(); // le micro sert à l'appel
        if (Relay is not { } relay)
        {
            ShowStatus("Le serveur de mise en relation des appels n'est pas encore en place : l'appel sera disponible dans une prochaine version.", Severity.Warning);
            return;
        }
        _isHost = host;
        Begin();
        _rawCode = code;
        var generation = _generation;
        if (await StartEngineAsync(generation) is not { } engine) return;
        var settings = _settings.Get();
        engine.Send(new
        {
            cmd = host ? "host" : "join",
            relay = relay.GetLeftPart(UriPartial.Authority),
            code,
            stun = settings.CallUseStun,
            mic = settings.CallMicrophone ?? "",
            speaker = settings.CallSpeaker ?? "",
            noise = NoiseMode(settings.CallNoiseSuppression),
            gate = GateMode(settings.CallGateMode),
            threshold = settings.CallGateThreshold,
            kbps = (int)settings.CallVoiceQuality,
        });
        ArmTimeout(TimeSpan.FromSeconds(20), "Le serveur de mise en relation ne répond pas : vérifiez votre connexion à Internet, puis recommencez.");
    }

    [RelayCommand(CanExecute = nameof(IsWaiting))]
    private void CopyCode()
    {
        try
        {
            System.Windows.Clipboard.SetText(Code);
            ShowStatus("Code copié : envoyez-le à votre ami.", Severity.Success);
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

    /// <summary>Raccrocher (ou annuler) : l'ami est prévenu, le micro et le moteur sont libérés.</summary>
    [RelayCommand(CanExecute = nameof(IsActive))]
    private void HangUp() => End(Phase == CallPhase.Connected ? "Appel terminé." : "", Severity.Info, tellPeer: true);

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

    // ===== Raccourci du micro (en jeu) =====

    /// <summary>« Ctrl + Alt + M », ou « Aucun ».</summary>
    public string MicKeyLabel => MicHotkey.Parse(_settings.Get().CallMicKey)?.Label(RawKeyboardListener.KeyName) ?? "Aucun";

    public bool HasMicKey => MicHotkey.Parse(_settings.Get().CallMicKey) is not null;

    /// <summary>Saisie du raccourci en cours : la prochaine touche (ou combinaison) pressée dans la fenêtre devient le raccourci.</summary>
    [ObservableProperty]
    private bool _isCapturingMicKey;

    public bool IsPushToTalk
    {
        get => _settings.Get().CallMicKeyMode == MicHotkeyMode.PushToTalk;
        set
        {
            if (value == IsPushToTalk) return;
            _settings.Update(s => s.CallMicKeyMode = value ? MicHotkeyMode.PushToTalk : MicHotkeyMode.Toggle);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsToggleMode));
            _log.Info($"Appel : raccourci en mode {(value ? "appuyer pour parler" : "basculer")}.");
        }
    }

    public bool IsToggleMode
    {
        get => !IsPushToTalk;
        set => IsPushToTalk = !value;
    }

    [RelayCommand]
    private void ChooseMicKey() => IsCapturingMicKey = true;

    [RelayCommand]
    private void CancelMicKeyCapture() => IsCapturingMicKey = false;

    [RelayCommand(CanExecute = nameof(HasMicKey))]
    private void ClearMicKey() => SetMicKey(null);

    /// <summary>Touche pressée pendant la saisie (vue) : Échap annule, un modificateur seul attend la touche principale.</summary>
    public void CaptureMicKey(int virtualKey, bool ctrl, bool alt, bool shift)
    {
        if (!IsCapturingMicKey) return;
        if (virtualKey == 0x1B)
        {
            IsCapturingMicKey = false;
            return;
        }
        if (MicHotkey.IsModifier(virtualKey)) return;
        SetMicKey(new MicHotkey(virtualKey, ctrl, alt, shift));
    }

    private void SetMicKey(MicHotkey? hotkey)
    {
        IsCapturingMicKey = false;
        _settings.Update(s => s.CallMicKey = hotkey?.Serialize());
        OnPropertyChanged(nameof(MicKeyLabel));
        OnPropertyChanged(nameof(HasMicKey));
        ClearMicKeyCommand.NotifyCanExecuteChanged();
        _log.Info(hotkey is null ? "Appel : raccourci du micro retiré." : $"Appel : raccourci du micro « {hotkey.Label(RawKeyboardListener.KeyName)} ».");
        UpdateKeyboard();
    }

    /// <summary>Clavier écouté pendant un appel en cours seulement, et seulement si un raccourci est choisi.</summary>
    private void UpdateKeyboard()
    {
        var hotkey = Phase == CallPhase.Connected ? MicHotkey.Parse(_settings.Get().CallMicKey) : null;
        if (hotkey is null)
        {
            _keyboard?.Dispose();
            _keyboard = null;
            _matcher = null;
            return;
        }
        _matcher = new MicHotkeyMatcher(hotkey);
        if (_keyboard is not null) return;
        try
        {
            var keyboard = new RawKeyboardListener();
            keyboard.KeyChanged += OnRawKey;
            keyboard.Start();
            _keyboard = keyboard;
        }
        catch (InvalidOperationException ex)
        {
            _log.Error("Appel : lecture du clavier impossible", ex);
            ShowStatus("Le raccourci du micro ne fonctionne pas sur ce PC (détails dans le journal).", Severity.Warning);
        }
    }

    // Thread d'écoute du clavier → thread de l'interface.
    private void OnRawKey(int virtualKey, bool isDown)
    {
        var change = _matcher?.OnKey(virtualKey, isDown) ?? 0;
        if (change == 0) return;
        _dispatcher.BeginInvoke(() => OnMicKey(change == 1));
    }

    /// <summary>Raccourci pressé (true) ou relâché (false).</summary>
    internal void OnMicKey(bool pressed)
    {
        if (Phase != CallPhase.Connected) return;
        if (IsPushToTalk)
        {
            SetMuted(!pressed);
        }
        else if (pressed)
        {
            SetMuted(!IsMuted);
            _engine?.Send(new { cmd = "cue", on = !IsMuted }); // son de confirmation : on ne voit pas OptiGame en jeu
        }
    }

    private void SetMuted(bool muted)
    {
        if (IsMuted == muted) return;
        IsMuted = muted;
        _engine?.Send(new { cmd = "mute", value = muted });
    }

    partial void OnPhaseChanged(CallPhase value) => UpdateKeyboard();

    partial void OnVolumeChanged(double value) => _engine?.Send(new { cmd = "volume", value = Math.Clamp(value, 0, 100) / 100 });

    partial void OnSelectedMicrophoneChanged(CallDevice? value)
    {
        if (_updatingDevices || value is null) return;
        _settings.Update(s => s.CallMicrophone = value.Id.Length == 0 ? null : value.Id);
        _engine?.Send(new { cmd = "mic", id = value.Id });
        _log.Info($"Appel : micro choisi « {value.Label} ».");
    }

    partial void OnSelectedSpeakerChanged(CallDevice? value)
    {
        if (_updatingDevices || value is null) return;
        _settings.Update(s => s.CallSpeaker = value.Id.Length == 0 ? null : value.Id);
        _engine?.Send(new { cmd = "speaker", id = value.Id });
        _log.Info($"Appel : sortie audio choisie « {value.Label} ».");
    }

    private void Begin()
    {
        _generation++;
        _micName = "";
        _sentKbps = 0; // le débit est envoyé quand le micro est connu (mode automatique : selon lui)
        ClearStatus();
        CanOpenMicrophoneSettings = false;
        Code = "";
        Countdown = "";
        SafetyWords = "";
        IsMuted = true;
        PeerMuted = false;
        LocalLevel = RemoteLevel = 0;
        Duration = "";
        ConnectionPath = "";
        _received = _sent = 0;
        _path = "";
        Phase = CallPhase.Preparing;
        OnPropertyChanged(nameof(WorkingText));
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
            case "noise":
                if (message.TryGetProperty("filter", out var filter) && filter.ValueKind == JsonValueKind.False
                    && _settings.Get().CallNoiseSuppression == NoiseSuppression.Strong)
                {
                    ShowStatus("Le filtre de bruit n'a pas pu démarrer sur ce PC : la suppression standard est utilisée.", Severity.Warning);
                }
                break;
            case "mic":
                OnMicLabel(Text(message, "label"));
                break;
            case "micBandwidth":
                OnMicBandwidth(message);
                break;
            case "voiceStats":
                OnVoiceStats(message);
                break;
            case "noiseUnavailable":
                _log.Warn($"Appel : filtre de bruit indisponible ({Text(message, "message")}).");
                break;
            case "devices":
                OnDevices(message);
                break;
            case "deviceLost":
                var input = Text(message, "kind") == "input";
                _settings.Update(s => { if (input) s.CallMicrophone = null; else s.CallSpeaker = null; });
                ShowStatus(input ? "Le micro choisi n'est plus branché : celui de Windows par défaut le remplace."
                    : "La sortie audio choisie n'est plus branchée : celle de Windows par défaut la remplace.", Severity.Warning);
                break;
            case "waiting" when Phase == CallPhase.Preparing && _isHost:
                _timeout?.Cancel();
                _expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)Number(message, "expiresAt"));
                Code = CallCode.Display(_rawCode);
                Phase = CallPhase.Waiting;
                UpdateCountdown();
                _ticker.Start();
                RingOutgoing();
                _log.Info("Appel : code créé, en attente de l'ami.");
                break;
            case "peer":
                _ticker.Stop();
                Countdown = "";
                Phase = CallPhase.Connecting;
                _log.Info($"Appel : {(_isHost ? "l'ami est arrivé" : "appel trouvé")}, connexion.");
                ArmTimeout(ConnectTimeout, null);
                break;
            case "fingerprints":
                SafetyWords = Core.Call.SafetyWords.For(Text(message, "local"), Text(message, "remote"));
                break;
            case "relayError":
                OnRelayError(Text(message, "code"));
                break;
            case "relayLeft":
                End(_isHost ? "Votre ami a annulé." : "Votre ami a annulé l'appel.", Severity.Info, tellPeer: false);
                break;
            case "relayClosed":
                _log.Warn($"Appel : serveur de mise en relation fermé (code {Number(message, "code")}, étape {Phase}).");
                End("Le serveur de mise en relation est injoignable : vérifiez votre connexion à Internet, puis recommencez.", Severity.Error, tellPeer: false);
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

    private void OnRelayError(string code)
    {
        _log.Warn($"Appel : refus du serveur de mise en relation ({code}, étape {Phase}).");
        if (code == "busy" && _isHost && Phase == CallPhase.Preparing && _codeRetries++ < 3)
        {
            // Code déjà pris (rare) : un autre, sans rien dire.
            var friend = _outgoing;
            End("", Severity.Info, tellPeer: false);
            _outgoing = friend;
            NotifyWaitingTexts();
            _ = OpenAsync(host: true, CallCode.Create());
            return;
        }
        End(code switch
        {
            "unknown" => "Ce code n'existe pas ou a expiré : demandez-en un nouveau à votre ami.",
            "busy" => "Quelqu'un d'autre a déjà rejoint cet appel.",
            "expired" when Phase == CallPhase.Waiting => "Personne n'a rejoint en 2 minutes : le code a expiré.",
            "expired" => "La connexion a pris trop de temps. Recommencez.",
            _ => "Le serveur de mise en relation a refusé l'appel. Recommencez dans un instant.",
        }, code == "expired" && Phase == CallPhase.Waiting ? Severity.Info : Severity.Warning, tellPeer: false);
    }

    private void OnDevices(JsonElement message)
    {
        var settings = _settings.Get();
        _updatingDevices = true;
        try
        {
            Fill(Microphones, message, "inputs", "defaultInput", "Par défaut");
            Fill(Speakers, message, "outputs", "defaultOutput", "Par défaut");
            SelectedMicrophone = Microphones.FirstOrDefault(d => d.Id == (settings.CallMicrophone ?? "")) ?? Microphones.FirstOrDefault();
            SelectedSpeaker = Speakers.FirstOrDefault(d => d.Id == (settings.CallSpeaker ?? "")) ?? Speakers.FirstOrDefault();
        }
        finally
        {
            _updatingDevices = false;
        }
    }

    private static void Fill(ObservableCollection<CallDevice> target, JsonElement message, string listName, string defaultName, string defaultLabel)
    {
        var current = Text(message, defaultName);
        var devices = new List<CallDevice> { new("", current.Length > 0 ? $"{defaultLabel} : {WithoutDefaultPrefix(current)}" : defaultLabel) };
        if (message.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            devices.AddRange(list.EnumerateArray().Select(d => new CallDevice(Text(d, "id"), Text(d, "label"))).Where(d => d.Id.Length > 0));
        }
        if (target.SequenceEqual(devices)) return;
        target.Clear();
        foreach (var device in devices) target.Add(device);
    }

    // Le moteur nomme l'entrée par défaut « Par défaut - Casque (…) » : seul le nom du périphérique est gardé.
    private static string WithoutDefaultPrefix(string label)
    {
        var dash = label.IndexOf(" - ", StringComparison.Ordinal);
        return dash is > 0 and < 20 ? label[(dash + 3)..] : label;
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

    private void UpdateCountdown()
    {
        if (Phase != CallPhase.Waiting) return;
        var left = _expiresAt - DateTimeOffset.UtcNow;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        Countdown = $"Valable encore {left:m\\:ss}";
        // Le serveur ferme le salon à l'échéance ; s'il ne l'a pas dit 5 s après, le code est tenu pour expiré ici.
        if (left == TimeSpan.Zero && DateTimeOffset.UtcNow > _expiresAt.AddSeconds(5)) OnRelayError("expired");
    }

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
            default:
                End("L'appel s'est arrêté sur une erreur (détails dans le journal).", Severity.Error, tellPeer: false);
                break;
        }
    }

    /// <summary>Délai d'une étape ; null = message de connexion impossible.</summary>
    private void ArmTimeout(TimeSpan delay, string? message)
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
            End(message ?? FailureMessage(), Severity.Error, tellPeer: false);
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Fin de l'appel : moteur détruit (micro libéré), page remise au départ, message gardé.</summary>
    private void End(string status, Severity severity, bool tellPeer)
    {
        StopOutgoing();
        _timeout?.Cancel();
        _ticker.Stop();
        _generation++;
        if (Phase == CallPhase.Connected)
        {
            _log.Info($"Appel terminé après {Duration} ({_received / 1024} Ko reçus, {_sent / 1024} Ko envoyés).");
            JoinCode = ""; // code utilisé : plus rien à rejoindre
        }
        var engine = _engine;
        _engine = null;
        if (engine is not null)
        {
            engine.Send(new { cmd = "hangup" });
            if (tellPeer)
            {
                // Le « raccroché » part avant la destruction du moteur.
                _ = Task.Delay(600).ContinueWith(_ => engine.Dispose(), TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                engine.Dispose();
            }
        }
        Code = "";
        Countdown = "";
        SafetyWords = "";
        IsMuted = true;
        PeerMuted = false;
        LocalLevel = RemoteLevel = 0;
        Phase = CallPhase.Idle;
        ResetMeter();
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

    /// <summary>Fermeture d'OptiGame : l'ami est prévenu si possible, le micro libéré.</summary>
    public void Dispose()
    {
        _ticker.Stop();
        _keyboard?.Dispose();
        _keyboard = null;
        _testEngine?.Dispose();
        _testEngine = null;
        if (_engine is { } engine)
        {
            engine.Send(new { cmd = "hangup" });
            engine.Dispose();
            _engine = null;
        }
    }
}

/// <summary>Choix de suppression du bruit proposé.</summary>
public sealed record NoiseOption(NoiseSuppression Mode, string Label)
{
    public override string ToString() => Label;
}
