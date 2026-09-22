using System.Text.Json;
using StackExchange.Redis;

namespace agot_bg_website.Infrastructure.Chat;

/// <summary>Public user data associated with one or more live chat connections.</summary>
public sealed record ConnectedUserData(
    string Username,
    bool IsAdmin,
    bool IsHighMember,
    string? LastWonTournament
);

internal sealed record ConnectedUserConnectionData(Guid UserId, ConnectedUserData User);

/// <summary>
/// Redis-backed presence for the public chat room. Each WebSocket has its own expiring record, so
/// concurrent connects, disconnects, and heartbeats never overwrite another connection's state.
/// </summary>
public sealed class ChatPresenceService(IConnectionMultiplexer redis)
{
    internal static readonly TimeSpan ConnectionLifetime = TimeSpan.FromHours(1);

    private const string AllPresenceKeysPattern = "chat:room:*:connected_user*";

    private IDatabase Db => redis.GetDatabase();

    private static string ConnectionsKey(Guid roomId) =>
        $"chat:room:{roomId}:connected_user_connections";

    private static string ConnectionKey(Guid roomId, Guid connectionId) =>
        $"chat:room:{roomId}:connected_user_connection:{connectionId}";

    private static string VersionKey(Guid roomId) => $"chat:room:{roomId}:connected_users_version";

    public async Task AddConnectedUserAsync(
        Guid roomId,
        Guid connectionId,
        Guid userId,
        ConnectedUserData userData
    )
    {
        var transaction = Db.CreateTransaction();
        _ = transaction.StringSetAsync(
            ConnectionKey(roomId, connectionId),
            JsonSerializer.Serialize(new ConnectedUserConnectionData(userId, userData)),
            ConnectionLifetime
        );
        _ = transaction.SetAddAsync(ConnectionsKey(roomId), connectionId.ToString());
        _ = transaction.StringIncrementAsync(VersionKey(roomId));
        if (!await transaction.ExecuteAsync())
        {
            throw new InvalidOperationException("Failed to add the chat presence record.");
        }
    }

    public async Task RemoveConnectedUserAsync(Guid roomId, Guid connectionId)
    {
        var transaction = Db.CreateTransaction();
        _ = transaction.KeyDeleteAsync(ConnectionKey(roomId, connectionId));
        _ = transaction.SetRemoveAsync(ConnectionsKey(roomId), connectionId.ToString());
        _ = transaction.StringIncrementAsync(VersionKey(roomId));
        if (!await transaction.ExecuteAsync())
        {
            throw new InvalidOperationException("Failed to remove the chat presence record.");
        }
    }

    public Task<bool> RefreshConnectionAsync(Guid roomId, Guid connectionId) =>
        Db.KeyExpireAsync(ConnectionKey(roomId, connectionId), ConnectionLifetime);

    public async Task<(
        Dictionary<Guid, ConnectedUserData> Users,
        List<Guid> PrunedConnectionIds,
        long Version
    )> GetConnectedUsersAsync(Guid roomId)
    {
        var prunedConnectionIds = new HashSet<Guid>();
        while (true)
        {
            var versionBefore = await GetVersionAsync(roomId);
            var snapshot = await ReadConnectionsAsync(roomId);
            if (snapshot.PrunedConnectionIds.Count > 0 || snapshot.MalformedValues.Length > 0)
            {
                prunedConnectionIds.UnionWith(snapshot.PrunedConnectionIds);
                var transaction = Db.CreateTransaction();
                var staleIndexValues = snapshot
                    .MalformedValues.Concat(
                        snapshot.PrunedConnectionIds.Select(id => (RedisValue)id.ToString())
                    )
                    .ToArray();
                _ = transaction.SetRemoveAsync(ConnectionsKey(roomId), staleIndexValues);
                _ = transaction.StringIncrementAsync(VersionKey(roomId));
                if (!await transaction.ExecuteAsync())
                {
                    throw new InvalidOperationException(
                        "Failed to prune stale chat presence records."
                    );
                }

                continue;
            }

            var versionAfter = await GetVersionAsync(roomId);
            if (versionBefore == versionAfter)
            {
                return (
                    CollapseConnections(snapshot.LiveConnections),
                    [.. prunedConnectionIds],
                    versionAfter
                );
            }
        }
    }

    private async Task<(
        List<ConnectedUserConnectionData> LiveConnections,
        List<Guid> PrunedConnectionIds,
        RedisValue[] MalformedValues
    )> ReadConnectionsAsync(Guid roomId)
    {
        var indexKey = ConnectionsKey(roomId);
        var indexedValues = await Db.SetMembersAsync(indexKey);
        var indexedConnections = indexedValues
            .Select(value => Guid.TryParse(value.ToString(), out var id) ? id : (Guid?)null)
            .ToList();

        var malformedValues = indexedValues
            .Where((_, index) => indexedConnections[index] is null)
            .ToArray();
        var connectionIds = indexedConnections.OfType<Guid>().ToArray();
        var values =
            connectionIds.Length == 0
                ? []
                : await Db.StringGetAsync(
                    connectionIds.Select(id => (RedisKey)ConnectionKey(roomId, id)).ToArray()
                );

        var liveConnections = new List<ConnectedUserConnectionData>(values.Length);
        var prunedConnectionIds = new List<Guid>();
        for (var index = 0; index < values.Length; index++)
        {
            if (
                values[index].IsNullOrEmpty
                || JsonSerializer.Deserialize<ConnectedUserConnectionData>(values[index].ToString())
                    is not { } connection
            )
            {
                prunedConnectionIds.Add(connectionIds[index]);
                continue;
            }

            liveConnections.Add(connection);
        }

        return (liveConnections, prunedConnectionIds, malformedValues);
    }

    internal static Dictionary<Guid, ConnectedUserData> CollapseConnections(
        IEnumerable<ConnectedUserConnectionData> connections
    ) =>
        connections
            .GroupBy(connection => connection.UserId)
            .ToDictionary(group => group.Key, group => group.First().User);

    private async Task<long> GetVersionAsync(Guid roomId)
    {
        var value = await Db.StringGetAsync(VersionKey(roomId));
        return value.IsNullOrEmpty ? 0 : (long)value;
    }

    /// <summary>Clears legacy and current presence keys before this single website process starts.</summary>
    public async Task ClearAllConnectedUsersAsync()
    {
        foreach (var endpoint in redis.GetEndPoints())
        {
            var server = redis.GetServer(endpoint);
            await foreach (var key in server.KeysAsync(pattern: AllPresenceKeysPattern))
            {
                await Db.KeyDeleteAsync(key);
            }
        }
    }
}
