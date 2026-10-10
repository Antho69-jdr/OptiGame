using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace OptiGame.App.Call;

/// <summary>
/// Copie locale du serveur de mise en relation (server/call-relay/worker.js, même protocole), sur ws://localhost : vérifications
/// du mode capture seulement, jamais utilisée par l'appli. Délai d'attente réglable pour tester l'expiration.
/// </summary>
internal sealed partial class LocalCallRelay : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly TimeSpan _wait;

    private sealed class Room
    {
        public WebSocket? Host;
        public WebSocket? Guest;
        public int Count;
        public CancellationTokenSource Expiry = new();
    }

    public LocalCallRelay(TimeSpan wait)
    {
        _wait = wait;
        var port = 50000 + Random.Shared.Next(10000);
        Url = new Uri($"ws://localhost:{port}");
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public Uri Url { get; }

    /// <summary>Messages relayés (vérifications).</summary>
    public int Relayed;

    [GeneratedRegex("^/v1/rooms/([23456789ABCDEFGHJKMNPQRSTUVWXYZ]{6})$")]
    private static partial Regex RoomPath();

    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            _ = HandleAsync(context);
        }
    }

    // ===== Amis en ligne (même protocole que la classe Presence du serveur) =====
    // Jeton de TEST seulement (jamais accepté par le vrai serveur) : « test:<steamid>:<nom>:<amis séparés par des virgules> ».

    private sealed record PresenceUser(string Id, string Name, string[] Friends, WebSocket Socket)
    {
        public string[] Contacts { get; set; } = [];
    }

    private readonly ConcurrentDictionary<string, PresenceUser> _presence = new();

    private static bool AreFriends(PresenceUser a, PresenceUser b) =>
        a.Friends.Contains(b.Id) || b.Friends.Contains(a.Id) || (a.Contacts.Contains(b.Id) && b.Contacts.Contains(a.Id));

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    private async Task PresenceAsync(HttpListenerContext context)
    {
        var parts = (context.Request.Headers["Authorization"] ?? "").Replace("Bearer ", "").Split(':');
        if (parts.Length != 4 || parts[0] != "test" || !context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 401;
            context.Response.Close();
            return;
        }
        var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
        var me = new PresenceUser(parts[1], parts[2], parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries), socket);
        if (_presence.TryGetValue(me.Id, out var old))
        {
            await SendAsync(old.Socket, """{"t":"replaced"}""");
            await CloseAsync(old.Socket);
        }
        _presence[me.Id] = me;
        var online = _presence.Values.Where(u => u.Id != me.Id && AreFriends(me, u)).ToList();
        foreach (var friend in online) await SendAsync(friend.Socket, Json(new { t = "online", friend = new { id = me.Id, name = me.Name } }));
        await SendAsync(socket, Json(new { t = "hello", you = new { id = me.Id, name = me.Name }, online = online.Select(u => new { id = u.Id, name = u.Name }),
            friendsListPublic = me.Friends.Length > 0 }));

        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (text == """{"t":"ping"}""")
                {
                    await SendAsync(socket, """{"t":"pong"}""");
                    continue;
                }
                using var document = System.Text.Json.JsonDocument.Parse(text);
                var root = document.RootElement;
                var type = root.GetProperty("t").GetString();
                var to = root.TryGetProperty("to", out var toValue) ? toValue.GetString() ?? "" : "";
                var code = root.TryGetProperty("code", out var codeValue) ? codeValue.GetString() ?? "" : "";
                if (type == "contacts")
                {
                    var before = _presence.Values.Where(u => u.Id != me.Id && AreFriends(me, u)).Select(u => u.Id).ToHashSet();
                    me.Contacts = root.GetProperty("ids").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
                    var after = _presence.Values.Where(u => u.Id != me.Id && AreFriends(me, u)).ToList();
                    foreach (var other in after.Where(u => !before.Contains(u.Id)))
                    {
                        await SendAsync(socket, Json(new { t = "online", friend = new { id = other.Id, name = other.Name } }));
                        await SendAsync(other.Socket, Json(new { t = "online", friend = new { id = me.Id, name = me.Name } }));
                    }
                    foreach (var gone in before.Where(id => after.All(u => u.Id != id)))
                    {
                        await SendAsync(socket, Json(new { t = "offline", id = gone }));
                        if (_presence.TryGetValue(gone, out var goneUser)) await SendAsync(goneUser.Socket, Json(new { t = "offline", id = me.Id }));
                    }
                    continue;
                }
                if (type is "request" or "accept")
                {
                    if (_presence.TryGetValue(to, out var recipient))
                    {
                        await SendAsync(recipient.Socket, Json(new { t = type == "request" ? "friendRequest" : "friendAccepted", from = new { id = me.Id, name = me.Name } }));
                    }
                    continue;
                }
                if (type is not ("call" or "decline" or "cancel")) continue;
                if (!_presence.TryGetValue(to, out var target) || !AreFriends(me, target))
                {
                    if (type == "call") await SendAsync(socket, Json(new { t = "callError", to, reason = target is null ? "offline" : "notFriend" }));
                    continue;
                }
                var forwarded = type switch { "call" => "ring", "decline" => "declined", _ => "cancelled" };
                // Invitation dans un appel de groupe : noms de ceux qui y sont déjà (6 au plus, 64 caractères chacun).
                var already = type == "call" && root.TryGetProperty("with", out var withValue) && withValue.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? withValue.EnumerateArray().Select(e => e.GetString() ?? "").Where(n => n.Length > 0).Take(6).Select(n => n[..Math.Min(64, n.Length)]).ToArray()
                    : null;
                await SendAsync(target.Socket, already is null
                    ? Json(new { t = forwarded, from = new { id = me.Id, name = me.Name }, code })
                    : Json(new { t = forwarded, from = new { id = me.Id, name = me.Name }, code, with = already }));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or System.Text.Json.JsonException or KeyNotFoundException) { }
        if (_presence.TryRemove(new KeyValuePair<string, PresenceUser>(me.Id, me)))
        {
            foreach (var friend in _presence.Values.Where(u => AreFriends(me, u))) await SendAsync(friend.Socket, Json(new { t = "offline", id = me.Id }));
        }
        await CloseAsync(socket);
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        if (await TryGroupAsync(context)) return;
        if (context.Request.Url!.AbsolutePath == "/v1/presence")
        {
            await PresenceAsync(context);
            return;
        }
        var match = RoomPath().Match(context.Request.Url!.AbsolutePath);
        var role = context.Request.QueryString["role"];
        if (!match.Success || !context.Request.IsWebSocketRequest || role is not ("host" or "guest"))
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }
        var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
        var code = match.Groups[1].Value;
        var room = _rooms.GetOrAdd(code, _ => new Room());
        string? error;
        lock (room)
        {
            error = role == "host" ? (room.Host is not null || room.Guest is not null ? "busy" : null)
                : room.Host is null ? "unknown" : room.Guest is not null ? "busy" : null;
            if (error is null && role == "host") room.Host = socket;
            if (error is null && role == "guest") room.Guest = socket;
        }
        if (error is not null)
        {
            if (room.Host is null && room.Guest is null) _rooms.TryRemove(code, out _);
            await SendAsync(socket, $$"""{"t":"error","code":"{{error}}"}""");
            await CloseAsync(socket);
            return;
        }
        if (role == "host")
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(_wait).ToUnixTimeMilliseconds();
            await SendAsync(socket, $$"""{"t":"waiting","expiresAt":{{expiresAt}}}""");
            ArmExpiry(code, room, _wait);
        }
        else
        {
            ArmExpiry(code, room, TimeSpan.FromMinutes(2));
            await SendAsync(socket, """{"t":"peer"}""");
            await SendAsync(room.Host!, """{"t":"peer"}""");
        }
        await ReceiveLoopAsync(code, room, socket, role == "host");
    }

    private void ArmExpiry(string code, Room room, TimeSpan delay)
    {
        room.Expiry.Cancel();
        room.Expiry = new CancellationTokenSource();
        _ = Task.Delay(delay, room.Expiry.Token).ContinueWith(_ => CloseRoomAsync(code, room, "expired"), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private async Task ReceiveLoopAsync(string code, Room room, WebSocket socket, bool isHost)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (!result.EndOfMessage || ++room.Count > 200)
                {
                    await CloseRoomAsync(code, room, "limit");
                    return;
                }
                var other = isHost ? room.Guest : room.Host;
                if (other is not null)
                {
                    Interlocked.Increment(ref Relayed);
                    await SendAsync(other, Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
            }
        }
        catch (WebSocketException) { }
        // Départ de l'un : l'autre est prévenu, le salon est effacé.
        var leaver = socket;
        foreach (var other in new[] { room.Host, room.Guest }.Where(s => s is not null && s != leaver))
        {
            await SendAsync(other!, """{"t":"left"}""");
            await CloseAsync(other!);
        }
        await CloseAsync(socket);
        room.Expiry.Cancel();
        _rooms.TryRemove(new KeyValuePair<string, Room>(code, room));
    }

    private async Task CloseRoomAsync(string code, Room room, string error)
    {
        foreach (var socket in new[] { room.Host, room.Guest })
        {
            if (socket is null) continue;
            await SendAsync(socket, $$"""{"t":"error","code":"{{error}}"}""");
            await CloseAsync(socket);
        }
        _rooms.TryRemove(new KeyValuePair<string, Room>(code, room));
    }

    // Un seul envoi à la fois par WebSocket (deux boucles peuvent écrire au même PC).
    private static readonly ConditionalWeakTable<WebSocket, SemaphoreSlim> SendLocks = [];

    private static async Task SendAsync(WebSocket socket, string text)
    {
        var gate = SendLocks.GetValue(socket, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open) await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            gate.Release();
        }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException) { }
    }

    public void Dispose()
    {
        _listener.Close();
    }
}
