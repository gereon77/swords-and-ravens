using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace agot_bg_website.Infrastructure.Chat;

/// <summary>
/// Tracks this process's live WebSocket connections per chat room, so a Redis pub/sub message for
/// a room (see <see cref="ChatBroadcaster"/>) can be relayed to exactly the local sockets that
/// belong to it — equivalent of Django Channels' local per-worker delivery once a group_send has
/// reached a given worker. See MIGRATION_PLAN.md §7.
/// </summary>
public sealed record ChatConnection(Guid ConnectionId, WebSocket Socket, Guid UserId);

public sealed class ChatConnectionManager
{
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<Guid, ChatConnection>
    > _connectionsByRoom = new();

    public Guid Add(Guid roomId, WebSocket socket, Guid userId)
    {
        var connectionId = Guid.NewGuid();
        var room = _connectionsByRoom.GetOrAdd(
            roomId,
            _ => new ConcurrentDictionary<Guid, ChatConnection>()
        );
        room[connectionId] = new ChatConnection(connectionId, socket, userId);
        return connectionId;
    }

    public void Remove(Guid roomId, Guid connectionId)
    {
        if (_connectionsByRoom.TryGetValue(roomId, out var room))
        {
            room.TryRemove(connectionId, out _);
        }
    }

    public IReadOnlyCollection<ChatConnection> GetConnections(Guid roomId) =>
        _connectionsByRoom.TryGetValue(roomId, out var room) ? [.. room.Values] : [];

    /// <summary>
    /// All of a user's live connections across every room, regardless of process-wide room count -
    /// used to force-close a user's chat sockets on logout (see Areas.Identity.Pages.Account.Logout)
    /// so they don't linger in the public room's "online users" presence list (ChatPresenceService)
    /// after their auth cookie has already been cleared.
    /// </summary>
    public IReadOnlyCollection<ChatConnection> GetConnectionsByUser(Guid userId) =>
        [
            .. _connectionsByRoom
                .Values.SelectMany(room => room.Values)
                .Where(c => c.UserId == userId),
        ];
}
