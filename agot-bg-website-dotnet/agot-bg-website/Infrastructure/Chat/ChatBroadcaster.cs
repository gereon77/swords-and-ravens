using System.Text.Json;
using StackExchange.Redis;

namespace agot_bg_website.Infrastructure.Chat;

/// <summary>
/// Cross-instance chat fan-out via Redis pub/sub, replacing Django Channels'
/// <c>channel_layer.group_send</c> (chat/consumers.py) — see MIGRATION_PLAN.md §7. One Redis
/// channel per room (<c>chat:room:{roomId}</c>); every instance subscribes to all of them via a
/// pattern subscription and relays incoming messages to whichever of its own local WebSocket
/// connections belong to that room (<see cref="ChatConnectionManager"/>). Registered as both a
/// singleton (for <see cref="PublishAsync"/>) and an <see cref="IHostedService"/> (to subscribe
/// once at startup).
/// </summary>
public sealed class ChatBroadcaster(IConnectionMultiplexer redis, ChatConnectionManager connections, ILogger<ChatBroadcaster> logger)
    : IHostedService
{
    private const string ChannelPrefix = "chat:room:";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var subscriber = redis.GetSubscriber();
        subscriber.Subscribe(RedisChannel.Pattern($"{ChannelPrefix}*"), async (channel, message) =>
        {
            try
            {
                await RelayToLocalConnectionsAsync(channel, message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to relay a chat pub/sub message from channel {Channel}", channel);
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        redis.GetSubscriber().UnsubscribeAll();
        return Task.CompletedTask;
    }

    /// <summary>Publishes a JSON-serializable payload to every instance subscribed to this room.</summary>
    public Task PublishAsync<T>(Guid roomId, T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return redis.GetSubscriber().PublishAsync(RedisChannel.Literal(ChannelName(roomId)), json);
    }

    private async Task RelayToLocalConnectionsAsync(RedisChannel channel, RedisValue message)
    {
        var channelName = channel.ToString();
        if (!channelName.StartsWith(ChannelPrefix, StringComparison.Ordinal) ||
            !Guid.TryParse(channelName[ChannelPrefix.Length..], out var roomId))
        {
            return;
        }

        var localConnections = connections.GetConnections(roomId);
        if (localConnections.Count == 0)
        {
            return;
        }

        var json = message.ToString();
        using var doc = JsonDocument.Parse(json!);
        var type = doc.RootElement.GetProperty("type").GetString();

        if (type == "__prune_check__")
        {
            // Internal-only: never forwarded verbatim. Each locally-connected user whose id was
            // pruned as stale gets a personalized force_disconnect, mirroring Django's
            // close_stale_connections (one consumer instance per user connection).
            var prunedUserIds = doc.RootElement.GetProperty("user_ids").EnumerateArray()
                .Select(e => e.GetGuid())
                .ToHashSet();

            var forceDisconnectJson = JsonSerializer.Serialize(new ForceDisconnectEvent());
            var bytes = System.Text.Encoding.UTF8.GetBytes(forceDisconnectJson);
            foreach (var connection in localConnections.Where(c => prunedUserIds.Contains(c.UserId)))
            {
                if (connection.Socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await connection.Socket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }

            return;
        }

        var rawBytes = System.Text.Encoding.UTF8.GetBytes(json!);
        foreach (var connection in localConnections)
        {
            if (connection.Socket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                await connection.Socket.SendAsync(rawBytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    private static string ChannelName(Guid roomId) => $"{ChannelPrefix}{roomId}";
}
