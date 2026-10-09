using System.Collections.ObjectModel;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OptiGame.Core.Call;
using OptiGame.Core.Logging;
using OptiGame.Core.Settings;
using OptiGame.Platform.Artwork;
using OptiGame.Platform.Library;
using OptiGame.Platform.Processes;

namespace OptiGame.App.Call;

/// <summary>État de la liaison avec les amis Steam.</summary>
public enum FriendsLinkState
{
    SignedOut,
    SigningIn,
    Connecting,
    Online,
    /// <summary>Connecté à Steam mais pas au serveur (invisible, Internet coupé : nouvel essai espacé).</summary>
    Offline,
}

/// <summary>Ami Steam qui a OptiGame ouvert.</summary>
public sealed record CallFriend(string Id, string Name)
{
    public string DisplayName => Name.Length > 0 ? Name : "Ami Steam";

    public string Initial => DisplayName[..1].ToUpperInvariant();
}

/// <summary>
/// Amis Steam des appels. Connexion avec Steam dans le navigateur (OpenID, vérifiée par le serveur de mise en relation, qui remet un
/// jeton de 30 jours gardé chiffré) ; puis une WebSocket au repos vers le serveur tant qu'OptiGame tourne et que l'utilisateur est
/// visible : liste de ses amis Steam qui ont OptiGame ouvert, appels sortants et entrants (sonnerie = code d'un salon). Pas de
/// scrutation : un message toutes les 45 s pour garder la connexion ouverte, nouvel essai espacé (5 s → 5 min) si elle tombe.
/// Singleton ; événements levés sur le thread de l'interface.
/// </summary>
public sealed partial class SteamFriendsService : ObservableObject, IDisposable
{
    private static readonly TimeSpan[] Retry = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];
    private static readonly TimeSpan Ping = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    private readonly AppSettingsStore _settings;
    private readonly FileLog _log;
    private readonly GameLauncher _launcher;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _run;
    private CancellationTokenSource? _login;
    private ClientWebSocket? _socket;
    private bool _signInCancelled;

    public SteamFriendsService(AppSettingsStore settings, FileLog log, GameLauncher launcher)
    {
        _settings = settings;
        _log = log;
        _launcher = launcher;
        _state = IsSignedIn ? FriendsLinkState.Offline : FriendsLinkState.SignedOut;
        OnlineFriends.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowsNoFriends));
    }

    /// <summary>Serveur de mise en relation (le même que les appels).</summary>
    internal Uri? Relay { get; set; } = CallRelay.Resolve(Environment.GetEnvironmentVariable(CallRelay.OverrideVariable));

    /// <summary>Jeton fourni directement (vérifications du mode capture, serveur local) : rien n'est enregistré.</summary>
    internal string? TestToken { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(IsOnline), nameof(IsSigningIn), nameof(StateText), nameof(ShowsNoFriends), nameof(ShowsPrivateListHint))]
    private FriendsLinkState _state;

    /// <summary>Dernier message pour l'utilisateur (erreur de connexion…) ; vide = aucun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = "";

    public bool HasMessage => Message.Length > 0;

    /// <summary>Liste d'amis Steam publique : sinon seuls les amis dont la liste est publique vous voient.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPrivateListHint))]
    private bool _friendsListPublic = true;

    public ObservableCollection<CallFriend> OnlineFriends { get; } = [];

    /// <summary>En ligne, mais aucun ami avec OptiGame ouvert.</summary>
    public bool ShowsNoFriends => IsOnline && OnlineFriends.Count == 0;

    /// <summary>Liste d'amis Steam privée : seuls les amis dont la liste est publique vous voient.</summary>
    public bool ShowsPrivateListHint => IsOnline && !FriendsListPublic;

    public bool IsSignedIn => Token is not null;

    public bool IsOnline => State == FriendsLinkState.Online;

    public bool IsSigningIn => State == FriendsLinkState.SigningIn;

    public string MyName => _settings.Get().CallSteamName is { Length: > 0 } name ? name : "votre compte Steam";

    public string StateText => State switch
    {
        FriendsLinkState.Online => "En ligne pour vos amis Steam",
        FriendsLinkState.Connecting => "Connexion…",
        FriendsLinkState.Offline when !IsVisible => "Invisible : vos amis ne vous voient pas et ne peuvent pas vous appeler",
        FriendsLinkState.Offline => "Hors ligne (nouvel essai bientôt)",
        _ => "",
    };

    /// <summary>Visible de ses amis Steam qui ont OptiGame (et joignable) ; réglage gardé.</summary>
    public bool IsVisible
    {
        get => _settings.Get().CallVisibleToFriends;
        set
        {
            if (value == IsVisible) return;
            _settings.Update(s => s.CallVisibleToFriends = value);
            OnPropertyChanged();
            if (value) Start();
            else Stop();
            OnPropertyChanged(nameof(StateText));
        }
    }

    /// <summary>Un ami appelle (ami, code du salon).</summary>
    public event Action<CallFriend, string>? Ring;

    /// <summary>L'ami appelé a refusé.</summary>
    public event Action<CallFriend, string>? Declined;

    /// <summary>L'ami qui appelait a raccroché avant la réponse.</summary>
    public event Action<CallFriend, string>? Cancelled;

    /// <summary>Appel impossible (ami parti, plus amis) : identifiant de l'ami, raison.</summary>
    public event Action<string, string>? CallError;

    /// <summary>Quelqu'un demande à devenir ami OptiGame.</summary>
    public event Action<CallFriend>? FriendRequest;

    /// <summary>Une demande d'ami envoyée a été acceptée (l'ami est ajouté).</summary>
    public event Action<CallFriend>? FriendAccepted;

    private string? Token => TestToken ?? SecretProtector.Unprotect(_settings.Get().CallSteamTokenProtected);

    // ===== Connexion avec Steam =====

    /// <summary>Ouvre la page de connexion de Steam dans le navigateur et attend le jeton (5 minutes au plus).</summary>
    public async Task SignInAsync()
    {
        if (Relay is not { } relay)
        {
            Message = "Le serveur de mise en relation n'est pas en place.";
            return;
        }
        _login?.Cancel();
        _login = new CancellationTokenSource(LoginTimeout);
        var cancel = _login.Token;
        var loginState = SteamFriendsLink.NewLoginState();
        _signInCancelled = false;
        Message = "";
        State = FriendsLinkState.SigningIn;
        try
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(SteamFriendsLink.LoginWait(relay, loginState), cancel);
            _launcher.OpenSteamLogin(SteamFriendsLink.LoginPage(relay, loginState));
            var text = await ReceiveTextAsync(socket, cancel);
            using var document = JsonDocument.Parse(text ?? "{}");
            var root = document.RootElement;
            var token = Text(root, "token");
            var id = Text(root, "id");
            if (SteamFriendsLink.ReadToken(token) is not { } read || read.SteamId != id)
            {
                throw new InvalidDataException("Réponse de connexion incomplète.");
            }
            var name = Text(root, "name");
            _settings.Update(s =>
            {
                s.CallSteamTokenProtected = SecretProtector.Protect(token);
                s.CallSteamName = name;
            });
            _log.Info("Amis Steam : connecté avec Steam.");
            Message = OtherLocalAccount(id) ? "Attention : ce compte Steam n'est pas celui du client Steam de ce PC." : "";
            OnPropertyChanged(nameof(MyName));
            State = FriendsLinkState.Offline;
            Start();
        }
        catch (OperationCanceledException)
        {
            Message = _signInCancelled ? "" : "Connexion avec Steam abandonnée (5 minutes sans réponse).";
            State = IsSignedIn ? FriendsLinkState.Offline : FriendsLinkState.SignedOut;
        }
        catch (Exception ex) when (ex is WebSocketException or InvalidDataException or JsonException or ArgumentException or System.Net.Http.HttpRequestException)
        {
            _log.Warn($"Amis Steam : connexion avec Steam impossible ({ex.Message}).");
            Message = "La connexion avec Steam n'a pas abouti. Vérifiez votre connexion à Internet, puis recommencez.";
            State = IsSignedIn ? FriendsLinkState.Offline : FriendsLinkState.SignedOut;
        }
    }

    public void CancelSignIn()
    {
        _signInCancelled = true;
        _login?.Cancel();
    }

    public void SignOut()
    {
        Stop();
        _settings.Update(s =>
        {
            s.CallSteamTokenProtected = null;
            s.CallSteamName = null;
        });
        _log.Info("Amis Steam : déconnecté de Steam.");
        Message = "";
        State = FriendsLinkState.SignedOut;
        OnPropertyChanged(nameof(MyName));
    }

    /// <summary>Le compte connecté n'est pas celui du client Steam de ce PC (avertissement, pas un blocage).</summary>
    private static bool OtherLocalAccount(string steamId)
    {
        var config = SteamPlaytimeReader.LocalConfigPath();
        var accountDirectory = config is null ? null : Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(config)));
        return uint.TryParse(accountDirectory, out var accountId) && SteamFriendsLink.FromAccountId(accountId) != steamId;
    }

    // ===== Présence =====

    /// <summary>Connexion au serveur (si connecté avec Steam et visible) ; sans effet si elle tourne déjà.</summary>
    public void Start()
    {
        if (_run is not null || !IsSignedIn || !IsVisible || Relay is null) return;
        _run = new CancellationTokenSource();
        _ = RunAsync(Relay, _run.Token);
    }

    public void Stop()
    {
        _run?.Cancel();
        _run = null;
        OnlineFriends.Clear();
        if (State is FriendsLinkState.Online or FriendsLinkState.Connecting) State = FriendsLinkState.Offline;
        OnPropertyChanged(nameof(StateText));
    }

    private async Task RunAsync(Uri relay, CancellationToken cancel)
    {
        var attempt = 0;
        while (!cancel.IsCancellationRequested)
        {
            OnUi(() => State = FriendsLinkState.Connecting);
            using var socket = new ClientWebSocket();
            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("Authorization", $"Bearer {Token}");
            var stop = false;
            try
            {
                await socket.ConnectAsync(SteamFriendsLink.Presence(relay), cancel);
                _socket = socket;
                attempt = 0;
                using var pingStop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                _ = PingAsync(socket, pingStop.Token);
                stop = await ReceiveLoopAsync(socket, cancel);
                pingStop.Cancel();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException) when (socket.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                _log.Warn("Amis Steam : jeton refusé par le serveur (expiré) : reconnexion avec Steam nécessaire.");
                OnUi(() =>
                {
                    _run = null;
                    SignOut();
                    Message = "Votre connexion avec Steam a expiré (30 jours) : reconnectez-vous.";
                });
                return;
            }
            catch (WebSocketException ex)
            {
                _log.Warn($"Amis Steam : serveur injoignable ({ex.Message}).");
            }
            finally
            {
                _socket = null;
                OnUi(() => OnlineFriends.Clear());
            }
            if (stop || cancel.IsCancellationRequested) return;
            OnUi(() => State = FriendsLinkState.Offline);
            try
            {
                await Task.Delay(Retry[Math.Min(attempt++, Retry.Length - 1)], cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PingAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        try
        {
            while (!cancel.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(Ping, cancel);
                await SendRawAsync(socket, """{"t":"ping"}""");
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
    }

    /// <summary>Messages du serveur jusqu'à la fermeture ; true = ne pas se reconnecter (connexion remplacée par un autre PC).</summary>
    private async Task<bool> ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        while (socket.State == WebSocketState.Open)
        {
            var text = await ReceiveTextAsync(socket, cancel);
            if (text is null) return false;
            JsonElement message;
            try
            {
                using var document = JsonDocument.Parse(text);
                message = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }
            if (Text(message, "t") == "replaced")
            {
                _log.Info("Amis Steam : connexion reprise par un autre OptiGame (autre PC) : celle-ci s'arrête.");
                OnUi(() =>
                {
                    _run = null;
                    State = FriendsLinkState.Offline;
                    Message = "OptiGame s'est connecté à vos amis Steam depuis un autre PC : ce PC n'est plus visible. Rouvrez la page Appel pour reprendre la main.";
                });
                return true;
            }
            OnUi(() => Handle(message));
        }
        return false;
    }

    private void Handle(JsonElement message)
    {
        switch (Text(message, "t"))
        {
            case "hello":
                OnlineFriends.Clear();
                foreach (var friend in Array(message, "online").Select(Friend)) OnlineFriends.Add(friend);
                FriendsListPublic = !message.TryGetProperty("friendsListPublic", out var isPublic) || isPublic.ValueKind != JsonValueKind.False;
                if (message.TryGetProperty("you", out var you) && Text(you, "name") is { Length: > 0 } name && name != _settings.Get().CallSteamName)
                {
                    _settings.Update(s => s.CallSteamName = name);
                    OnPropertyChanged(nameof(MyName));
                }
                Message = "";
                State = FriendsLinkState.Online;
                SendContacts(); // amis OptiGame de ce PC : le serveur ne les garde que le temps de la connexion
                _log.Info($"Amis Steam : en ligne, {OnlineFriends.Count} ami(s) avec OptiGame ouvert, liste d'amis {(FriendsListPublic ? "publique" : "privée")}.");
                break;
            case "online" when message.TryGetProperty("friend", out var arrived):
                var added = Friend(arrived);
                if (OnlineFriends.All(f => f.Id != added.Id)) OnlineFriends.Add(added);
                break;
            case "offline":
                var id = Text(message, "id");
                foreach (var gone in OnlineFriends.Where(f => f.Id == id).ToList()) OnlineFriends.Remove(gone);
                break;
            case "ring" when message.TryGetProperty("from", out var caller):
                Ring?.Invoke(Friend(caller), Text(message, "code"));
                break;
            case "declined" when message.TryGetProperty("from", out var decliner):
                Declined?.Invoke(Friend(decliner), Text(message, "code"));
                break;
            case "cancelled" when message.TryGetProperty("from", out var canceller):
                Cancelled?.Invoke(Friend(canceller), Text(message, "code"));
                break;
            case "friendRequest" when message.TryGetProperty("from", out var requester):
                FriendRequest?.Invoke(Friend(requester));
                break;
            case "friendAccepted" when message.TryGetProperty("from", out var accepter):
                var accepted = Friend(accepter);
                if (!_pendingRequests.Remove(accepted.Id)) break; // acceptation d'une demande jamais envoyée : ignorée
                AddContact(new CallContact(accepted.Id, accepted.Name));
                FriendAccepted?.Invoke(accepted);
                break;
            case "callError":
                CallError?.Invoke(Text(message, "to"), Text(message, "reason"));
                break;
        }
    }

    // ===== Amis OptiGame (demandes d'ami, accord gardé sur ce PC) =====

    private readonly HashSet<string> _pendingRequests = [];

    /// <summary>Amis OptiGame acceptés des deux côtés (settings.json).</summary>
    public IReadOnlyList<CallContact> Contacts => _settings.Get().CallContacts;

    /// <summary>Personnes connues du client Steam de ce PC, à qui proposer une demande d'ami (relu à chaque appel).</summary>
    public IReadOnlyList<SteamPersona> KnownPeople()
    {
        try
        {
            if (SteamPlaytimeReader.LocalConfigPath() is not { } config) return [];
            var account = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(config)));
            return SteamPersonas.Read(File.ReadAllText(config), uint.TryParse(account, out var id) ? id : 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _log.Warn($"Amis Steam : noms connus de Steam illisibles ({ex.Message}).");
            return [];
        }
    }

    /// <summary>Demande d'ami : remise seulement si l'autre a OptiGame ouvert ; on ne sait ni s'il l'a reçue ni s'il refuse.</summary>
    public void SendFriendRequest(SteamPersona person)
    {
        _pendingRequests.Add(person.SteamId);
        Send(new { t = "request", to = person.SteamId });
        _log.Info("Amis Steam : demande d'ami envoyée.");
    }

    public void AcceptFriendRequest(CallFriend from)
    {
        AddContact(new CallContact(from.Id, from.Name));
        Send(new { t = "accept", to = from.Id });
        _log.Info("Amis Steam : demande d'ami acceptée.");
    }

    public void RemoveContact(string steamId)
    {
        _settings.Update(s => s.CallContacts = s.CallContacts.Where(c => c.SteamId != steamId).ToList());
        OnPropertyChanged(nameof(Contacts));
        SendContacts();
        _log.Info("Amis Steam : ami OptiGame retiré.");
    }

    private void AddContact(CallContact contact)
    {
        _settings.Update(s => s.CallContacts = [.. s.CallContacts.Where(c => c.SteamId != contact.SteamId), contact]);
        OnPropertyChanged(nameof(Contacts));
        SendContacts();
    }

    private void SendContacts() => Send(new { t = "contacts", ids = _settings.Get().CallContacts.Select(c => c.SteamId).ToArray() });

    public void Call(string friendId, string code) => Send(new { t = "call", to = friendId, code });

    public void Decline(string friendId, string code) => Send(new { t = "decline", to = friendId, code });

    public void Cancel(string friendId, string code) => Send(new { t = "cancel", to = friendId, code });

    private void Send(object message)
    {
        if (_socket is { State: WebSocketState.Open } socket) _ = SendRawAsync(socket, JsonSerializer.Serialize(message));
    }

    private async Task SendRawAsync(ClientWebSocket socket, string text)
    {
        await _sendLock.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open) await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException) { }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        var buffer = new byte[8192];
        using var text = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancel);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            text.Write(buffer, 0, result.Count);
            if (text.Length > 256 * 1024) return null;
            if (result.EndOfMessage) return Encoding.UTF8.GetString(text.ToArray());
        }
    }

    private static CallFriend Friend(JsonElement element) => new(Text(element, "id"), Text(element, "name"));

    private static IEnumerable<JsonElement> Array(JsonElement message, string name) =>
        message.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private void OnUi(Action action) => _dispatcher.BeginInvoke(action);

    public void Dispose()
    {
        _login?.Cancel();
        _run?.Cancel();
        _run = null;
    }
}
