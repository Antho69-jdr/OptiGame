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

    private async Task HandleAsync(HttpListenerContext context)
    {
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
