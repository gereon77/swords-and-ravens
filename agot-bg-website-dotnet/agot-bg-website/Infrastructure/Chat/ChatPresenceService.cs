using System.Text.Json;
using StackExchange.Redis;

namespace agot_bg_website.Infrastructure.Chat;

/// <summary>One connected-user entry as tracked for the public room's presence list.</summary>
public sealed record ConnectedUserData(string Username, bool IsAdmin, bool IsHighMember, string? LastWonTournament)
{
    public int Count { get; set; } = 1;
    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Redis-backed replacement for Django's <c>get/add/remove_connected_user</c> cache helpers
/// (chat/consumers.py) — tracks who's currently connected to the public room's chat, for the
/// "online users" list shown on the website. A single JSON blob per room (matching Django's single
/// cache key holding a dict), stored with no expiration — staleness is pruned per-entry, same as
/// the Python implementation (<see cref="StaleAfter"/>). See MIGRATION_PLAN.md §7.
/// </summary>
public sealed class ChatPresenceService(IConnectionMultiplexer redis)
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private static string CacheKey(Guid roomId) => $"chat:room:{roomId}:connected_users";

    private IDatabase Db => redis.GetDatabase();

    public async Task<Dictionary<Guid, ConnectedUserData>> AddConnectedUserAsync(Guid roomId, Guid userId, ConnectedUserData userData)
    {
        var users = await ReadAsync(roomId);
        if (users.TryGetValue(userId, out var existing))
        {
            existing.Count++;
            existing.LastActiveAt = DateTimeOffset.UtcNow;
        }
        else
        {
            users[userId] = userData;
        }

        await WriteAsync(roomId, users);
        return users;
    }

    public async Task<Dictionary<Guid, ConnectedUserData>> RemoveConnectedUserAsync(Guid roomId, Guid userId)
    {
        var users = await ReadAsync(roomId);
        if (users.TryGetValue(userId, out var existing))
        {
            existing.Count--;
            if (existing.Count <= 0)
            {
                users.Remove(userId);
            }
        }

        await WriteAsync(roomId, users);
        return users;
    }

    /// <summary>Returns the still-live users plus the ids of any stale entries pruned along the way.</summary>
    public async Task<(Dictionary<Guid, ConnectedUserData> Users, List<Guid> PrunedUserIds)> GetConnectedUsersAsync(Guid roomId)
    {
        var users = await ReadAsync(roomId);
        var cutoff = DateTimeOffset.UtcNow - StaleAfter;
        var stale = users.Where(kv => kv.Value.LastActiveAt < cutoff).Select(kv => kv.Key).ToList();
        foreach (var uid in stale)
        {
            users.Remove(uid);
        }

        if (stale.Count > 0)
        {
            await WriteAsync(roomId, users);
        }

        return (users, stale);
    }

    public async Task RefreshLastActiveAtAsync(Guid roomId, Guid userId)
    {
        var users = await ReadAsync(roomId);
        if (users.TryGetValue(userId, out var existing))
        {
            existing.LastActiveAt = DateTimeOffset.UtcNow;
            await WriteAsync(roomId, users);
        }
    }

    private async Task<Dictionary<Guid, ConnectedUserData>> ReadAsync(Guid roomId)
    {
        var json = await Db.StringGetAsync(CacheKey(roomId));
        if (json.IsNullOrEmpty)
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<Guid, ConnectedUserData>>(json.ToString()) ?? [];
    }

    private Task WriteAsync(Guid roomId, Dictionary<Guid, ConnectedUserData> users) =>
        Db.StringSetAsync(CacheKey(roomId), JsonSerializer.Serialize(users));
}
