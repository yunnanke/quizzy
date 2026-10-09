using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace QuizMvp.Services;

public sealed class QuizSocketHub(AppStore store)
{
    private readonly ConcurrentDictionary<WebSocket, SocketClient> _clients = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(WebSocket socket, AppUser user)
    {
        _clients[socket] = new SocketClient(user);
        var buffer = new byte[8192];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleMessageAsync(socket, user, text);
            }
        }
        finally
        {
            _clients.TryRemove(socket, out _);
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        }
    }

    public Task BroadcastRoomAsync(string code, object payload)
    {
        var tasks = _clients
            .Where(x => x.Value.RoomCode == code)
            .Select(x => SendAsync(x.Key, payload));
        return Task.WhenAll(tasks);
    }

    private async Task HandleMessageAsync(WebSocket socket, AppUser user, string text)
    {
        using var doc = JsonDocument.Parse(text);
        var type = doc.RootElement.GetProperty("type").GetString();
        var code = doc.RootElement.TryGetProperty("code", out var codeNode) ? codeNode.GetString()?.Trim().ToUpperInvariant() : null;

        if (type == "join-room" && !string.IsNullOrWhiteSpace(code))
        {
            if (_clients.TryGetValue(socket, out var client))
            {
                client.RoomCode = code;
            }
            await SendAsync(socket, store.BuildRoomState(code));
            await BroadcastRoomAsync(code, store.BuildRoomState(code));
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        switch (type)
        {
            case "start":
                if (user.Role == UserRole.Organizer)
                {
                    await store.StartSessionAsync(code, user.Id);
                    await BroadcastRoomAsync(code, store.BuildRoomState(code));
                }
                break;
            case "next":
                if (user.Role == UserRole.Organizer)
                {
                    await store.MoveNextAsync(code, user.Id);
                    await BroadcastRoomAsync(code, store.BuildRoomState(code));
                }
                break;
            case "end":
                if (user.Role == UserRole.Organizer)
                {
                    await store.EndSessionAsync(code, user.Id);
                    await BroadcastRoomAsync(code, store.BuildRoomState(code));
                }
                break;
            case "answer":
                var questionId = doc.RootElement.GetProperty("questionId").GetString() ?? "";
                var optionIds = doc.RootElement.GetProperty("optionIds").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
                await store.SubmitAnswerAsync(code, user.Id, questionId, optionIds);
                await BroadcastRoomAsync(code, store.BuildRoomState(code));
                break;
        }
    }

    private async Task SendAsync(WebSocket socket, object payload)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, _json);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private sealed class SocketClient(AppUser user)
    {
        public AppUser User { get; } = user;
        public string? RoomCode { get; set; }
    }
}
