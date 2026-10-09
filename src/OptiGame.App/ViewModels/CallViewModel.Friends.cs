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
