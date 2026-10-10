using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Call;
using OptiGame.App.Controls;
using OptiGame.Core.Call;

namespace OptiGame.App.ViewModels;

/// <summary>Participant d'un appel (salon de groupe) : nom annoncé, niveau, micro coupé, volume choisi ici, mots de contrôle.</summary>
public sealed partial class CallParticipant(string id, string name, Action<string, double> setVolume) : ObservableObject
{
    public string Id { get; } = id;

    public string DisplayName { get; } = name.Length > 0 ? name : "Ami";

    public string Initial => DisplayName[..1].ToUpperInvariant();

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _muted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _connected;

    /// <summary>4 mots tirés des empreintes de ce PC et de celui du participant : il doit voir les mêmes.</summary>
    [ObservableProperty]
    private string _safetyWords = "";

    /// <summary>Volume de ce participant, 0 à 100 (ce PC seulement).</summary>
    [ObservableProperty]
    private double _volume = 100;

    public string StateText => !Connected ? "connexion…" : Muted ? "micro coupé" : "";

    internal long Received { get; set; }

    internal string Path { get; set; } = "";

    partial void OnVolumeChanged(double value) => setVolume(Id, value);
}

/// <summary>
/// Appels de groupe (jusqu'à 6, voix directe entre tous) : participants du salon, arrivées et départs, invitations d'amis en cours
/// d'appel (chaque participant peut faire sonner un de SES amis), fin de l'appel quand il ne reste personne.
/// </summary>
public sealed partial class CallViewModel
{
    /// <summary>Taille maximale d'un appel (le serveur refuse au-delà).</summary>
    public const int MaxParticipants = 6;

    public ObservableCollection<CallParticipant> Participants { get; } = [];

    /// <summary>Amis invités pendant l'appel dont on attend la réponse : identifiant Steam → nom.</summary>
    private readonly Dictionary<string, string> _invites = [];

    private CallParticipant? First => Participants.FirstOrDefault();

    private string ParticipantNames => Participants.Count == 0 ? "" : string.Join(", ", Participants.Select(p => p.DisplayName));

    public bool CanInvite => Phase is CallPhase.Waiting or CallPhase.Connecting or CallPhase.Connected
        && Participants.Count + _invites.Count + 1 < MaxParticipants;

    /// <summary>
    /// Place de chaque ami dans l'appel en cours (nom → « dans l'appel » / « ça sonne… ») pour la liste « En ligne » : un ami qui y
    /// figure n'a pas de bouton « Inviter ». Remplacé à chaque changement (les liaisons ne voient pas les modifications d'un dictionnaire).
    /// </summary>
    [ObservableProperty]
    private IReadOnlyDictionary<string, string> _friendCallStates = new Dictionary<string, string>();

    /// <summary>« Inviter » un ami en ligne dans l'appel en cours : il reçoit la sonnerie avec la liste de ceux qui sont déjà là.</summary>
    [RelayCommand]
    private void InviteFriend(CallFriend? friend)
    {
        if (friend is null || !CanInvite || _rawCode.Length == 0) return;
        if (_invites.ContainsKey(friend.Id) || Participants.Any(p => p.DisplayName == friend.DisplayName)) return;
        var already = Participants.Select(p => p.DisplayName).Append(MyCallName()).ToArray();
        Friends.Call(friend.Id, _rawCode, already);
        _invites[friend.Id] = friend.DisplayName;
        NotifyParticipants();
        ShowStatus($"Ça sonne chez {friend.DisplayName}…", Severity.Info);
        _log.Info("Appel : un ami est invité dans l'appel.");
    }

    /// <summary>Nom annoncé aux autres participants : celui du compte Steam connecté.</summary>
    private string MyCallName() => _settings.Get().CallSteamName is { Length: > 0 } name ? name : "Ami";

    private CallParticipant AddParticipant(string id, string name)
    {
        if (Participants.FirstOrDefault(p => p.Id == id) is { } existing) return existing;
        var participant = new CallParticipant(id, name, (pid, value) => _engine?.Send(new { cmd = "volume", id = pid, value = Math.Clamp(value, 0, 100) / 100 }));
        Participants.Add(participant);
        // Un invité arrive : il ne sonne plus.
        foreach (var invite in _invites.Where(i => i.Value == participant.DisplayName).ToList()) _invites.Remove(invite.Key);
        NotifyParticipants();
        return participant;
    }

    private void NotifyParticipants()
    {
        OnPropertyChanged(nameof(CallHeader));
        OnPropertyChanged(nameof(CanInvite));
        OnPropertyChanged(nameof(PeerState));
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in _invites.Values) states[name] = "ça sonne…";
        foreach (var participant in Participants) states[participant.DisplayName] = "dans l'appel";
        FriendCallStates = states;
    }

    /// <summary>Membres déjà dans le salon (à l'arrivée, ou après une reprise du serveur).</summary>
    private void OnMembers(JsonElement message)
    {
        var resumed = message.TryGetProperty("resumed", out var r) && r.ValueKind == JsonValueKind.True;
        var members = message.TryGetProperty("members", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
        foreach (var member in members) AddParticipant(Text(member, "id"), Text(member, "name"));
        if (resumed) return;
        _timeout?.Cancel();
        if (members.Count == 0)
        {
            // Appel créé : personne encore, on fait sonner l'ami appelé.
            Code = CallCode.Display(_rawCode);
            Phase = CallPhase.Waiting;
            RingOutgoing();
            _log.Info("Appel : appel créé, en attente de l'ami.");
        }
        else
        {
            Phase = CallPhase.Connecting;
            _log.Info($"Appel : appel rejoint ({members.Count} participant(s) déjà là), connexion.");
            ArmTimeout(ConnectTimeout, null);
        }
    }

    private void OnJoined(JsonElement message)
    {
        if (!message.TryGetProperty("member", out var member)) return;
        var participant = AddParticipant(Text(member, "id"), Text(member, "name"));
        _log.Info("Appel : un participant arrive.");
        if (Phase == CallPhase.Waiting)
        {
            Phase = CallPhase.Connecting;
            ArmTimeout(ConnectTimeout, null);
        }
        else if (Phase == CallPhase.Connected)
        {
            ShowStatus($"{participant.DisplayName} rejoint l'appel.", Severity.Info);
        }
    }

    private void OnLeft(string id, string reason)
    {
        if (Participants.FirstOrDefault(p => p.Id == id) is not { } participant) return;
        Participants.Remove(participant);
        NotifyParticipants();
        _log.Info("Appel : un participant est parti.");
        if (Participants.Count > 0)
        {
            ShowStatus($"{participant.DisplayName} {reason}.", Severity.Info);
            return;
        }
        // Plus personne : l'appel s'arrête (sauf si des invités n'ont pas encore répondu).
        if (Phase == CallPhase.Connected && _invites.Count == 0) End($"{participant.DisplayName} {reason}.", Severity.Info, tellPeer: false);
        else if (Phase == CallPhase.Connecting) End($"{participant.DisplayName} a annulé l'appel.", Severity.Info, tellPeer: false);
    }

    private void OnPeerState(JsonElement message)
    {
        var id = Text(message, "id");
        var participant = Participants.FirstOrDefault(p => p.Id == id) ?? AddParticipant(id, Text(message, "name"));
        switch (Text(message, "value"))
        {
            case "connected":
                var wasConnected = participant.Connected;
                participant.Connected = true;
                if (Phase != CallPhase.Connected)
                {
                    _timeout?.Cancel();
                    _connectedAt = DateTime.Now;
                    ClearStatus();
                    Phase = CallPhase.Connected;
                    _log.Info("Appel : connecté.");
                }
                else if (!wasConnected)
                {
                    ClearStatus(); // reconnecté après une coupure, ou un nouveau participant
                }
                break;
            case "disconnected" when participant.Connected:
                ShowStatus($"Connexion interrompue avec {participant.DisplayName} : nouvelle tentative…", Severity.Warning);
                break;
            case "failed":
                _log.Warn($"Appel : connexion impossible ou perdue avec un participant (étape {Phase}, découverte d'adresse {(UseStun ? "activée" : "désactivée")}).");
                if (Participants.Count(p => p.Connected && p != participant) == 0 && Phase != CallPhase.Connected)
                {
                    End(FailureMessage(), Severity.Error, tellPeer: false);
                    return;
                }
                OnLeft(id, participant.Connected ? "n'est plus joignable (connexion perdue)" : "n'a pas pu être joint (réseau)");
                break;
        }
    }

    private void OnFingerprints(JsonElement message)
    {
        var participant = Participants.FirstOrDefault(p => p.Id == Text(message, "id"));
        var words = Core.Call.SafetyWords.For(Text(message, "local"), Text(message, "remote"));
        if (participant is not null) participant.SafetyWords = words;
        if (participant is null || participant == First) SafetyWords = words; // appel à deux : les mots de l'ami
    }

    private void OnPeerMuted(JsonElement message)
    {
        if (Participants.FirstOrDefault(p => p.Id == Text(message, "id")) is not { } participant) return;
        participant.Muted = message.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True;
        PeerMuted = First?.Muted ?? false;
    }

    private void OnGroupLevels(JsonElement message)
    {
        if (Phase != CallPhase.Connected) return;
        LocalLevel = Number(message, "local");
        _sent = (long)Number(message, "sent");
        long received = 0;
        double loudest = 0;
        if (message.TryGetProperty("peers", out var peers) && peers.ValueKind == JsonValueKind.Array)
        {
            foreach (var peer in peers.EnumerateArray())
            {
                if (Participants.FirstOrDefault(p => p.Id == Text(peer, "id")) is not { } participant) continue;
                participant.Level = Number(peer, "level");
                participant.Received = (long)Number(peer, "received");
                participant.Path = Text(peer, "path");
                received += participant.Received;
                loudest = Math.Max(loudest, participant.Level);
            }
        }
        RemoteLevel = loudest;
        _received = received;
        var path = First?.Path ?? "";
        if (path.Length > 0 && path != _path)
        {
            _path = path;
            ConnectionPath = path == "host/host" ? "directe, même réseau" : "directe, par Internet";
            _log.Info($"Appel : chemin {path}.");
        }
        var elapsed = DateTime.Now - _connectedAt;
        Duration = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
    }

    /// <summary>Fin de l'appel : les amis invités qui sonnent encore ne sonnent plus.</summary>
    private void CancelInvites()
    {
        foreach (var friendId in _invites.Keys) Friends.Cancel(friendId, _rawCode);
        _invites.Clear();
        Participants.Clear();
        NotifyParticipants();
    }

    /// <summary>Réponse d'un ami invité pendant l'appel : refus ou erreur (l'appel continue).</summary>
    private bool OnInviteAnswer(string friendId, string text)
    {
        if (!_invites.Remove(friendId, out var name)) return false;
        NotifyParticipants();
        ShowStatus($"{name} {text}", Severity.Info);
        return true;
    }
}
