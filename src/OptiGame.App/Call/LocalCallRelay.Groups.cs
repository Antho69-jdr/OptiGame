using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OptiGame.App.Call;

/// <summary>Salons de groupe (/v2/rooms) de la copie locale du serveur : même protocole que la classe GroupRoom de worker.js.</summary>
internal sealed partial class LocalCallRelay
{
    private sealed class GroupRoom
    {
        public readonly Dictionary<string, (string Name, WebSocket Socket)> Members = [];
    }

    private readonly ConcurrentDictionary<string, GroupRoom> _groups = new();

    [GeneratedRegex("^/v2/rooms/([23456789ABCDEFGHJKMNPQRSTUVWXYZ]{6})$")]
    private static partial Regex GroupPath();

    private async Task<bool> TryGroupAsync(HttpListenerContext context)
    {
        var match = GroupPath().Match(context.Request.Url!.AbsolutePath);
        if (!match.Success) return false;
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 426;
            context.Response.Close();
            return true;
        }
        var role = context.Request.QueryString["role"];
        var name = (context.Request.QueryString["name"] ?? "")[..Math.Min(64, (context.Request.QueryString["name"] ?? "").Length)];
        var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
        var room = _groups.GetOrAdd(match.Groups[1].Value, _ => new GroupRoom());
        string? error = null;
        string id;
        List<(string Id, string Name, WebSocket Socket)> others;
        lock (room)
        {
            var count = room.Members.Count;
            if (role == "create" && count > 0) error = "busy";
            else if (role == "join" && count == 0) error = "unknown";
            else if (role is not ("create" or "join" or "resume")) error = "unknown";
            else if (role != "resume" && count >= 6) error = "full";
            var requested = context.Request.QueryString["member"] ?? "";
            id = role == "resume" && requested.Length == 8 ? requested : Guid.NewGuid().ToString("N")[..8];
            others = room.Members.Where(m => m.Key != id).Select(m => (m.Key, m.Value.Name, m.Value.Socket)).ToList();
            if (error is null) room.Members[id] = (name, socket);
        }
        if (error is not null)
        {
            await SendAsync(socket, Json(new { t = "error", code = error }));
            await CloseAsync(socket);
            return true;
        }
        await SendAsync(socket, Json(new { t = "members", you = id, members = others.Select(o => new { id = o.Id, name = o.Name }) }));
        if (role != "resume")
        {
            foreach (var other in others) await SendAsync(other.Socket, Json(new { t = "joined", member = new { id, name } }));
        }

        var buffer = new byte[64 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)) is not JsonObject message) continue;
                var to = message["to"]?.GetValue<string>() ?? "";
                message.Remove("to");
                message["from"] = id; // l'expéditeur est celui de la connexion
                WebSocket? target;
                lock (room) target = room.Members.TryGetValue(to, out var member) ? member.Socket : null;
                if (target is not null)
                {
                    Interlocked.Increment(ref Relayed);
                    await SendAsync(target, message.ToJsonString());
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or JsonException or InvalidOperationException) { }

        List<WebSocket> remaining;
        lock (room)
        {
            if (room.Members.TryGetValue(id, out var current) && current.Socket == socket) room.Members.Remove(id);
            remaining = room.Members.Values.Select(m => m.Socket).ToList();
        }
        foreach (var other in remaining) await SendAsync(other, Json(new { t = "left", id }));
        await CloseAsync(socket);
        return true;
    }
}
