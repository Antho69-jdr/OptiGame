using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Call;
using OptiGame.App.Controls;
using OptiGame.Core.Call;

namespace OptiGame.App.ViewModels;

/// <summary>Choix proposé dans une liste (qualité, seuil).</summary>
public sealed record VoiceOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Voix envoyée : débit, seuil du micro (« noise gate »), mesure du niveau et « Tester mon micro ».</summary>
public sealed partial class CallViewModel
{
    private CallEngine? _testEngine;

    public IReadOnlyList<VoiceOption<VoiceQuality>> QualityOptions { get; } =
    [
        new(VoiceQuality.Auto, "Automatique (selon votre micro)"),
        new(VoiceQuality.Standard, "Standard (96 kbit/s)"),
        new(VoiceQuality.High, "Haute (128 kbit/s)"),
        new(VoiceQuality.Max, "Maximale (192 kbit/s)"),
    ];

    public VoiceOption<VoiceQuality> SelectedQuality
    {
        get => QualityOptions.First(o => o.Value == _settings.Get().CallVoiceQuality);
        set
        {
            if (value is null || value.Value == _settings.Get().CallVoiceQuality) return;
            _settings.Update(s => s.CallVoiceQuality = value.Value);
            _sentKbps = 0;
            ApplyQuality();
            _log.Info($"Appel : qualité de la voix « {value.Label} ».");
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<VoiceOption<MicGateMode>> GateOptions { get; } =
    [
        new(MicGateMode.Auto, "Automatique (recommandé)"),
        new(MicGateMode.Manual, "Manuel"),
        new(MicGateMode.Off, "Désactivé"),
    ];

    public VoiceOption<MicGateMode> SelectedGate
    {
        get => GateOptions.First(o => o.Value == _settings.Get().CallGateMode);
        set
        {
            if (value is null || value.Value == _settings.Get().CallGateMode) return;
            _settings.Update(s => s.CallGateMode = value.Value);
            SendGate();
            _log.Info($"Appel : seuil du micro « {value.Label} ».");
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManualGate));
        }
    }

    public bool IsManualGate => _settings.Get().CallGateMode == MicGateMode.Manual;

    /// <summary>Seuil manuel, en dBFS (-80 à -10).</summary>
    public double GateThreshold
    {
        get => _settings.Get().CallGateThreshold;
        set
        {
            var threshold = (int)Math.Round(Math.Clamp(value, -80, -10));
            if (threshold == _settings.Get().CallGateThreshold) return;
            _settings.Update(s => s.CallGateThreshold = threshold);
            SendGate();
            OnPropertyChanged();
        }
    }

    // ===== Débit automatique (selon le micro) =====

    private string _micName = "";
    private int _sentKbps;
    private readonly Dictionary<string, int?> _micSampleRates = [];

    /// <summary>« Votre micro capte la voix jusqu'à 9 kHz : 64 kbit/s, plus de débit n'apporterait rien. » (mode automatique).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoQualityText))]
    private string _autoQualityText = "";

    public bool HasAutoQualityText => AutoQualityText.Length > 0;

    /// <summary>Débit envoyé en ce moment (vérifications).</summary>
    internal int SentKbps => _sentKbps;

    /// <summary>Détail de la dernière mesure de bande (vérifications).</summary>
    internal string BandDiagnostics { get; private set; } = "";

    /// <summary>Micro ouvert (nom donné par le moteur web) : son format dans Windows et sa mesure gardée donnent le débit.</summary>
    private void OnMicLabel(string label)
    {
        _micName = VoiceQualityAdvisor.DeviceName(label);
        ApplyQuality();
    }

    /// <summary>Bande captée mesurée pendant qu'on parle : gardée pour ce micro (5 s de parole au moins), débit recalculé.</summary>
    private void OnMicBandwidth(JsonElement message)
    {
        if (message.TryGetProperty("diffs", out var diffs) && diffs.ValueKind == JsonValueKind.Array)
        {
            BandDiagnostics = $"{Number(message, "voiceFrames")} trames de voix, {Number(message, "silenceFrames")} de silence ; écart voix − silence par 500 Hz : " +
                string.Join(" ", diffs.EnumerateArray().Select(d => d.GetDouble().ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (_micName.Length == 0 || Number(message, "voiceFrames") < VoiceQualityAdvisor.MinVoiceFrames) return;
        var hz = (int)Number(message, "hz");
        if (_settings.Get().CallMicBandwidths.TryGetValue(_micName, out var known) && known == hz) return;
        _settings.Update(s => s.CallMicBandwidths[_micName] = hz);
        _log.Info($"Appel : bande captée par le micro « {_micName} » : {hz} Hz.");
        ApplyQuality();
    }

    /// <summary>Débit à envoyer : choisi, ou conseillé pour ce micro (mode automatique) ; envoyé au moteur s'il change.</summary>
    private void ApplyQuality()
    {
        var settings = _settings.Get();
        int kbps;
        if (settings.CallVoiceQuality == VoiceQuality.Auto)
        {
            // Micro de l'appel ; hors appel, celui choisi (ou celui de Windows par défaut) : le débit s'affiche aussi dans Paramètres › Audio.
            var name = _micName.Length > 0 ? _micName : CurrentMicName();
            var advice = VoiceQualityAdvisor.Recommend(SampleRateOf(name),
                settings.CallMicBandwidths.TryGetValue(name, out var hz) ? hz : null);
            kbps = advice.Kbps;
            AutoQualityText = advice.Reason;
        }
        else
        {
            kbps = (int)settings.CallVoiceQuality;
            AutoQualityText = "";
        }
        if (kbps == _sentKbps) return;
        _sentKbps = kbps;
        _engine?.Send(new { cmd = "quality", kbps });
        if (_micName.Length > 0) _log.Info($"Appel : débit de la voix {kbps} kbit/s ({(settings.CallVoiceQuality == VoiceQuality.Auto ? "automatique" : "choisi")}).");
    }

    /// <summary>Micro choisi dans les réglages, sinon celui de Windows par défaut ; vide si inconnu.</summary>
    private string CurrentMicName()
    {
        if (_settings.Get().CallMicrophone is { Length: > 0 } chosen) return chosen;
        try
        {
            return Platform.Audio.AudioEndpoints.DefaultCaptureName() ?? "";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return "";
        }
    }

    /// <summary>Fréquence d'échantillonnage du micro dans Windows (format partagé), retrouvé par son nom ; null si inconnu.</summary>
    private int? SampleRateOf(string name)
    {
        if (name.Length == 0) return null;
        if (_micSampleRates.TryGetValue(name, out var cached)) return cached;
        int? rate = null;
        try
        {
            rate = Platform.Audio.AudioEndpoints.Capture().FirstOrDefault(d => d.Name == name)?.SampleRate;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            _log.Warn($"Appel : format du micro illisible ({ex.Message}).");
        }
        _micSampleRates[name] = rate;
        return rate;
    }

    // ===== Mesure du niveau (pendant un appel ou un test) =====

    /// <summary>Niveau du micro après la suppression du bruit, 0 à 1 (−80 à 0 dBFS).</summary>
    [ObservableProperty]
    private double _micMeter;

    /// <summary>Position du seuil sur la même échelle (0 à 1) ; -1 = pas de seuil.</summary>
    [ObservableProperty]
    private double _gateMeter = -1;

    /// <summary>« Niveau −32 dB · seuil −45 dB · votre voix passe ».</summary>
    [ObservableProperty]
    private string _meterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMeter), nameof(MicTestLabel))]
    private bool _isTestingMic;

    public bool ShowsMeter => IsTestingMic || IsActive;

    public string MicTestLabel => IsTestingMic ? "Arrêter le test" : "Tester mon micro";

    /// <summary>« Tester mon micro » : micro et traitement seuls (aucun réseau), pour voir son niveau et régler le seuil.</summary>
    [RelayCommand]
    private async Task ToggleMicTestAsync()
    {
        if (IsTestingMic)
        {
            StopMicTest();
            return;
        }
        if (!IsIdle) return;
        IsTestingMic = true;
        try
        {
            var engine = await CallEngine.StartAsync(Path.Combine(_paths.Root, "webview-call"), _log);
            if (!IsTestingMic)
            {
                engine.Dispose();
                return;
            }
            _testEngine = engine;
            _micName = "";
            _sentKbps = 0;
            engine.Message += OnTestMessage;
            var settings = _settings.Get();
            engine.Send(new
            {
                cmd = "test",
                mic = settings.CallMicrophone ?? "",
                noise = NoiseMode(settings.CallNoiseSuppression),
                gate = GateMode(settings.CallGateMode),
                threshold = settings.CallGateThreshold,
                kbps = (int)settings.CallVoiceQuality,
            });
            _log.Info("Appel : test du micro.");
        }
        catch (Exception ex) when (ex is TimeoutException or FileNotFoundException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException
            or Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            _log.Error("Appel : test du micro impossible", ex);
            StopMicTest();
            ShowStatus("Le test du micro n'a pas pu démarrer (détails dans le journal).", Severity.Error);
        }
    }

    private void StopMicTest()
    {
        _testEngine?.Dispose();
        _testEngine = null;
        IsTestingMic = false;
        ResetMeter();
    }

    private void OnTestMessage(JsonElement message)
    {
        if (!message.TryGetProperty("ev", out var ev)) return;
        switch (ev.GetString())
        {
            case "mic":
                OnMicLabel(Text(message, "label"));
                break;
            case "micBandwidth":
                OnMicBandwidth(message);
                break;
            case "voiceStats":
                OnVoiceStats(message);
                break;
            case "devices":
                OnDevices(message);
                break;
            case "error":
                _log.Warn($"Appel : erreur pendant le test du micro ({Text(message, "name")}).");
                StopMicTest();
                if (Text(message, "name") == "NotAllowedError") CanOpenMicrophoneSettings = true;
                ShowStatus("Le micro est inaccessible : vérifiez qu'il est branché et autorisé dans Windows.", Severity.Warning);
                break;
        }
    }

    private void OnVoiceStats(JsonElement message)
    {
        var db = Number(message, "db");
        MicMeter = Math.Clamp((db + 80) / 80, 0, 1);
        var hasThreshold = message.TryGetProperty("threshold", out var threshold) && threshold.ValueKind == JsonValueKind.Number;
        GateMeter = hasThreshold ? Math.Clamp((threshold.GetDouble() + 80) / 80, 0, 1) : -1;
        NoiseVoice = Number(message, "vad");
        NoiseFrames = (long)Number(message, "frames");
        var open = !message.TryGetProperty("open", out var openValue) || openValue.ValueKind != JsonValueKind.False;
        GateOpen = open;
        FilterCpu = Number(message, "cpu");
        FilterEngine = Text(message, "engine");
        MeterText = hasThreshold
            ? $"Niveau {Db(db)} · seuil {Db(threshold.GetDouble())} · {(open ? "le son passe" : "coupé (sous le seuil)")}"
            : $"Niveau {Db(db)}";
    }

    /// <summary>Dernier état du seuil (vérifications).</summary>
    internal bool GateOpen { get; private set; } = true;

    /// <summary>Part d'un cœur prise par le filtre de bruit, et filtre réellement actif (« dfn », « rnnoise », « none ») : vérifications.</summary>
    internal double FilterCpu { get; private set; }

    internal string FilterEngine { get; private set; } = "";

    private static string Db(double db) => db <= -79.5 ? "silence" : $"{Math.Round(db):0} dB".Replace("-", "−");

    private void ResetMeter()
    {
        MicMeter = 0;
        GateMeter = -1;
        MeterText = "";
    }

    private void SendGate()
    {
        var settings = _settings.Get();
        var command = new { cmd = "gate", mode = GateMode(settings.CallGateMode), threshold = settings.CallGateThreshold };
        _engine?.Send(command);
        _testEngine?.Send(command);
    }

    private static string GateMode(MicGateMode mode) => mode switch
    {
        MicGateMode.Manual => "manual",
        MicGateMode.Off => "off",
        _ => "auto",
    };
}
