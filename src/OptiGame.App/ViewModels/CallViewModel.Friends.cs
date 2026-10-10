using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiGame.App.Call;
using OptiGame.App.Controls;

namespace OptiGame.App.ViewModels;

/// <summary>Appel entrant d'un ami Steam : qui, et le code du salon où le rejoindre.</summary>
public sealed record IncomingCall(CallFriend From, string Code);

/// <summary>Amis Steam de la page Appel : appeler un ami en un clic, répondre ou refuser un appel entrant.</summary>
public sealed partial class CallViewModel
{
    /// <summary>Sans réponse dans ce délai, l'appel entrant est refusé et signalé comme manqué.</summary>
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(30);

    private readonly Services.INotificationService _notifications;
    private CallFriend? _outgoing;
    private bool _ringSent;
    private CancellationTokenSource? _ringTimeout;

    /// <summary>Connexion avec Steam, amis en ligne.</summary>
    public SteamFriendsService Friends { get; }

    /// <summary>Appel entrant en attente de réponse ; null = aucun (fenêtre « … vous appelle » affichée tant qu'il existe).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomingCall), nameof(IncomingCallText))]
    [NotifyCanExecuteChangedFor(nameof(AnswerCallCommand), nameof(DeclineCallCommand))]
    private IncomingCall? _incomingCall;

    public bool HasIncomingCall => IncomingCall is not null;

    public string IncomingCallText => IncomingCall is { } call ? $"{call.From.DisplayName} vous appelle" : "";

    /// <summary>Titre de la carte du code : « Appel de Gaming… » quand on appelle un ami, sinon « Votre code ».</summary>
    public string WaitingTitle => _outgoing is { } friend ? $"Ça sonne chez {friend.DisplayName}…" : "Votre code";

    public string WaitingHelp => _outgoing is not null
        ? "Votre ami voit une fenêtre « Répondre / Refuser ». Il peut aussi saisir ce code dans « Rejoindre un ami »."
        : "Envoyez-le à votre ami : dès qu'il le saisit dans « Rejoindre un ami », l'appel commence.";

    [RelayCommand]
    private Task SignInSteamAsync() => Friends.SignInAsync();

    [RelayCommand]
    private void CancelSteamSignIn() => Friends.CancelSignIn();

    [RelayCommand]
    private void SignOutSteam() => Friends.SignOut();

    /// <summary>« Appeler » sur un ami en ligne : salon ouvert, puis sonnerie chez l'ami avec son code.</summary>
    [RelayCommand]
    private Task CallFriendAsync(CallFriend? friend)
    {
        if (friend is null || !IsIdle) return Task.CompletedTask;
        _codeRetries = 0;
        _outgoing = friend;
        CallPeerName = friend.DisplayName;
        _ringSent = false;
        NotifyWaitingTexts();
        _log.Info("Appel : appel d'un ami Steam.");
        return OpenAsync(host: true, Core.Call.CallCode.Create());
    }

    /// <summary>Le salon est ouvert (étape « waiting ») : la sonnerie part chez l'ami appelé.</summary>
    private void RingOutgoing()
    {
        if (_outgoing is not { } friend || _ringSent) return;
        Friends.Call(friend.Id, _rawCode);
        _ringSent = true;
    }

    /// <summary>Fin d'un appel sortant non abouti : la sonnerie s'arrête chez l'ami.</summary>
    private void StopOutgoing()
    {
        if (_outgoing is { } friend && _ringSent && Phase == CallPhase.Waiting) Friends.Cancel(friend.Id, _rawCode);
        _outgoing = null;
        _ringSent = false;
        NotifyWaitingTexts();
    }

    private void NotifyWaitingTexts()
    {
        OnPropertyChanged(nameof(WaitingTitle));
        OnPropertyChanged(nameof(WaitingHelp));
    }

    private bool CanAnswer() => IncomingCall is not null;

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task AnswerCallAsync()
    {
        if (IncomingCall is not { } call) return Task.CompletedTask;
        ClearIncoming();
        if (!IsIdle) End("", Severity.Info, tellPeer: true); // un code en attente : abandonné pour répondre
        JoinCode = Core.Call.CallCode.Display(call.Code);
        CallPeerName = call.From.DisplayName;
        _log.Info("Appel : appel d'un ami accepté.");
        return JoinCallAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private void DeclineCall()
    {
        if (IncomingCall is not { } call) return;
        ClearIncoming();
        Friends.Decline(call.From.Id, call.Code);
        _log.Info("Appel : appel d'un ami refusé.");
    }

    private void ClearIncoming()
    {
        _ringTimeout?.Cancel();
        IncomingCall = null;
    }

    private void OnRing(CallFriend from, string code)
    {
        // Déjà en appel, ou un autre appel sonne : refusé tout de suite (l'appelant voit « a refusé »).
        if (Phase is CallPhase.Connecting or CallPhase.Connected || IncomingCall is not null)
        {
            Friends.Decline(from.Id, code);
            _log.Info("Appel : appel d'un ami refusé (déjà occupé).");
            return;
        }
        IncomingCall = new IncomingCall(from, code);
        _log.Info("Appel : un ami appelle.");
        _ringTimeout?.Cancel();
        _ringTimeout = new CancellationTokenSource();
        var token = _ringTimeout.Token;
        _ = Task.Delay(RingTimeout, token).ContinueWith(_ =>
        {
            if (IncomingCall is not { } call || call.Code != code) return;
            ClearIncoming();
            Friends.Decline(call.From.Id, call.Code);
            Missed(call.From);
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnCancelled(CallFriend from, string code)
    {
        if (IncomingCall is not { } call || call.Code != code || call.From.Id != from.Id) return;
        ClearIncoming();
        Missed(from);
    }

    private void Missed(CallFriend from)
    {
        _log.Info("Appel : appel d'un ami manqué.");
        ShowStatus($"Appel manqué de {from.DisplayName}.", Severity.Info);
        _notifications.Show("Appel manqué", $"{from.DisplayName} vous a appelé.");
    }

    // ===== Amis OptiGame : demandes d'ami =====

    /// <summary>Demandes d'ami reçues, en attente de réponse.</summary>
    public System.Collections.ObjectModel.ObservableCollection<CallFriend> FriendRequests { get; } = [];

    /// <summary>Amis OptiGame (acceptés des deux côtés), avec « Retirer ».</summary>
    public System.Collections.ObjectModel.ObservableCollection<Core.Call.CallContact> ContactItems { get; } = [];

    public string ContactsHeader => $"Mes amis OptiGame ({ContactItems.Count})";

    /// <summary>Panneau « Ajouter un ami » ouvert : recherche dans les noms connus du client Steam.</summary>
    [ObservableProperty]
    private bool _isAddingFriend;

    [ObservableProperty]
    private string _friendSearch = "";

    public System.Collections.ObjectModel.ObservableCollection<Core.Call.SteamPersona> FriendSearchResults { get; } = [];

    private IReadOnlyList<Core.Call.SteamPersona> _knownPeople = [];

    [RelayCommand]
    private void ToggleAddFriend()
    {
        IsAddingFriend = !IsAddingFriend;
        if (!IsAddingFriend) return;
        _knownPeople = Friends.KnownPeople();
        FriendSearch = "";
        UpdateFriendSearch();
    }

    partial void OnFriendSearchChanged(string value) => UpdateFriendSearch();

    private void UpdateFriendSearch()
    {
        FriendSearchResults.Clear();
        var contacts = Friends.Contacts.Select(c => c.SteamId).ToHashSet();
        foreach (var person in Core.Call.SteamPersonas.Search(_knownPeople.Where(p => !contacts.Contains(p.SteamId)).ToList(), FriendSearch))
        {
            FriendSearchResults.Add(person);
        }
    }

    [RelayCommand]
    private void SendFriendRequest(Core.Call.SteamPersona? person)
    {
        if (person is null) return;
        Friends.SendFriendRequest(person);
        IsAddingFriend = false;
        ShowStatus($"Demande d'ami envoyée à {person.Name}. Elle lui arrive s'il a OptiGame ouvert et connecté à Steam ; vous le verrez en ligne dès qu'il l'aura acceptée.",
            Severity.Info);
    }

    [RelayCommand]
    private void AcceptFriendRequest(CallFriend? from)
    {
        if (from is null) return;
        FriendRequests.Remove(from);
        Friends.AcceptFriendRequest(from);
    }

    /// <summary>Ignorer : rien n'est envoyé (l'autre ne sait pas que sa demande est refusée).</summary>
    [RelayCommand]
    private void IgnoreFriendRequest(CallFriend? from)
    {
        if (from is not null) FriendRequests.Remove(from);
    }

    [RelayCommand]
    private void RemoveContact(Core.Call.CallContact? contact)
    {
        if (contact is not null) Friends.RemoveContact(contact.SteamId);
    }

    // ===== Page Amis : appel en cours et amis hors ligne =====

    /// <summary>Ami de l'appel en cours (appelé ou qui appelle) ; vide = aucun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CallHeader))]
    private string _callPeerName = "";

    /// <summary>« En appel avec Gaming · 3:12 », « Ça sonne chez Gaming… », « Connexion à Gaming… ».</summary>
    public string CallHeader => Phase switch
    {
        CallPhase.Connected => $"En appel avec {PeerOrFriend} · {Duration}",
        CallPhase.Waiting => $"Ça sonne chez {PeerOrFriend}…",
        CallPhase.Preparing or CallPhase.Connecting => $"Connexion à {PeerOrFriend}…",
        _ => "",
    };

    private string PeerOrFriend => CallPeerName.Length > 0 ? CallPeerName : "votre ami";

    /// <summary>Onglet du panneau d'appel : l'appel lui-même, les périphériques (et le volume de l'ami), ou le son envoyé.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCallTab), nameof(IsDevicesTab), nameof(IsSoundTab))]
    private int _inCallTab;

    public bool IsCallTab
    {
        get => InCallTab == 0;
        set { if (value) InCallTab = 0; }
    }

    public bool IsDevicesTab
    {
        get => InCallTab == 1;
        set
        {
            if (!value) return;
            InCallTab = 1;
            RefreshDevices();
        }
    }

    public bool IsSoundTab
    {
        get => InCallTab == 2;
        set { if (value) InCallTab = 2; }
    }

    /// <summary>Amis OptiGame qui n'ont pas OptiGame ouvert en ce moment (grisés, « Retirer »).</summary>
    public System.Collections.ObjectModel.ObservableCollection<Core.Call.CallContact> OfflineContacts { get; } = [];

    public bool HasOfflineContacts => OfflineContacts.Count > 0;

    private void RefreshOffline()
    {
        var online = Friends.OnlineFriends.Select(f => f.Id).ToHashSet();
        OfflineContacts.Clear();
        foreach (var contact in Friends.Contacts.Where(c => !online.Contains(c.SteamId)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            OfflineContacts.Add(contact);
        }
        OnPropertyChanged(nameof(HasOfflineContacts));
    }

    private void RefreshContacts()
    {
        RefreshOffline();
        ContactItems.Clear();
        foreach (var contact in Friends.Contacts.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)) ContactItems.Add(contact);
        OnPropertyChanged(nameof(ContactsHeader));
    }

    private void OnFriendRequest(CallFriend from)
    {
        if (Friends.Contacts.Any(c => c.SteamId == from.Id))
        {
            Friends.AcceptFriendRequest(from); // déjà ami de notre côté (l'autre a réinstallé OptiGame) : accord renvoyé
            return;
        }
        if (FriendRequests.Any(r => r.Id == from.Id)) return;
        FriendRequests.Add(from);
        _log.Info("Amis Steam : demande d'ami reçue.");
        _notifications.Show("Demande d'ami", $"{from.DisplayName} veut vous ajouter comme ami dans OptiGame (page Appel).");
    }

    private void OnFriendAccepted(CallFriend from) => ShowStatus($"{from.DisplayName} a accepté votre demande d'ami.", Severity.Success);

    private void OnDeclined(CallFriend from, string code)
    {
        if (_outgoing is not { } friend || friend.Id != from.Id || code != _rawCode) return;
        _outgoing = null; // pas de « cancel » en retour
        End($"{friend.DisplayName} a refusé l'appel.", Severity.Info, tellPeer: false);
    }

    private void OnCallError(string friendId, string reason)
    {
        if (_outgoing is not { } friend || friend.Id != friendId) return;
        _outgoing = null;
        End(reason == "notFriend" ? $"Vous n'êtes plus amis sur Steam avec {friend.DisplayName}." : $"{friend.DisplayName} n'a plus OptiGame ouvert.",
            Severity.Warning, tellPeer: false);
    }
}
