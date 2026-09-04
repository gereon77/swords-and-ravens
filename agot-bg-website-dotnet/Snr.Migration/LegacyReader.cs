using Npgsql;

namespace Snr.Migration;

/// <summary>
/// Read-only access to the legacy Django database, using raw SQL (no EF model of Django's schema
/// is needed/maintained — see MIGRATION_PLAN.md §10). Never writes to the legacy connection.
/// </summary>
public class LegacyReader(string connectionString)
{
    private NpgsqlConnection OpenConnection()
    {
        var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Opens a connection with a larger-than-default command timeout, for commands whose page size
    /// is deliberately kept small (see <see cref="ReadGamesAsync"/>) but that can still take a while
    /// per page because they transfer large JSON blobs (`serialized_game`) over a remote connection.
    /// The default Npgsql command timeout (30s) can be too short even to execute+return a single
    /// small page in that situation, independent of any risk from a long-lived, slowly-consumed
    /// reader - paging bounds how much a single command has to fetch, this bounds how long it's
    /// allowed to take doing so.
    /// </summary>
    private NpgsqlConnection OpenConnectionWithTimeout(int commandTimeoutSeconds)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            CommandTimeout = commandTimeoutSeconds,
        };
        var conn = new NpgsqlConnection(builder.ConnectionString);
        conn.Open();
        return conn;
    }

    public async IAsyncEnumerable<LegacyUser> ReadUsersAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, username, email, game_token, profile_text, last_won_tournament,
                   email_notification_active, mute_games, use_house_names_for_chat, use_map_scrollbar,
                   use_responsive_layout_on_mobile, last_username_update_time, last_activity,
                   vanilla_forum_user_id, date_joined
            FROM agotboardgame_main_user
            ORDER BY date_joined
            """,
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyUser(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) || reader.GetString(2).Length == 0 ? null : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetFieldValue<DateTimeOffset>(12),
                reader.GetInt32(13),
                reader.GetFieldValue<DateTimeOffset>(14)
            );
        }
    }

    public async IAsyncEnumerable<LegacyGroup> ReadGroupsAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, name FROM auth_group ORDER BY id",
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyGroup(reader.GetInt32(0), reader.GetString(1));
        }
    }

    public async IAsyncEnumerable<LegacyUserGroup> ReadUserGroupsAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, group_id FROM agotboardgame_main_user_groups ORDER BY user_id",
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyUserGroup(reader.GetGuid(0), reader.GetInt32(1));
        }
    }

    public async IAsyncEnumerable<LegacyRoom> ReadRoomsAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, name, public, max_retrieve_count, created_at
            FROM chat_room
            ORDER BY created_at
            """,
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyRoom(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetFieldValue<DateTimeOffset>(4)
            );
        }
    }

    public async IAsyncEnumerable<LegacyUserInRoom> ReadUsersInRoomAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, room_id, last_viewed_message_id FROM chat_userinroom ORDER BY room_id",
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyUserInRoom(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2)
            );
        }
    }

    /// <summary>
    /// This page query no longer selects `serialized_game` (see <see
    /// cref="ReadSerializedGamesByIdsAsync"/>) - only `view_of_game`, a small summary JSON object,
    /// so pages can be considerably larger than when this blob was still included inline. Keyset
    /// pagination is ordered by `created_at, id` (the same tiebreaker-safe pattern used by
    /// `ReadMessagesAsync`) rather than `OFFSET`, which would force Postgres to re-scan and discard
    /// all prior rows on every page. A page boundary is also a safe place to resume from if a run
    /// needs to be retried.
    /// </summary>
    public async IAsyncEnumerable<LegacyGame> ReadGamesAsync()
    {
        const int pageSize = 500;
        DateTimeOffset? afterCreatedAt = null;
        Guid? afterId = null;
        while (true)
        {
            var page = await ReadGamesPageAsync(afterCreatedAt, afterId, pageSize);
            if (page.Count == 0)
            {
                yield break;
            }
            foreach (var game in page)
            {
                yield return game;
            }
            var last = page[^1];
            afterCreatedAt = last.CreatedAt;
            afterId = last.Id;
        }
    }

    private async Task<List<LegacyGame>> ReadGamesPageAsync(
        DateTimeOffset? afterCreatedAt,
        Guid? afterId,
        int pageSize
    )
    {
        await using var conn = OpenConnectionWithTimeout(120);
        var sql = afterCreatedAt is null
            ? """
                SELECT id, name, owner_id, view_of_game::text, version, state,
                       created_at, updated_at, last_active_at
                FROM agotboardgame_main_game
                ORDER BY created_at, id
                LIMIT @pageSize
                """
            : """
                SELECT id, name, owner_id, view_of_game::text, version, state,
                       created_at, updated_at, last_active_at
                FROM agotboardgame_main_game
                WHERE (created_at, id) > (@afterCreatedAt, @afterId)
                ORDER BY created_at, id
                LIMIT @pageSize
                """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pageSize", pageSize);
        if (afterCreatedAt is { } createdAt)
        {
            cmd.Parameters.AddWithValue("afterCreatedAt", createdAt);
            cmd.Parameters.AddWithValue("afterId", afterId!.Value);
        }

        var result = new List<LegacyGame>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(
                new LegacyGame(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetFieldValue<DateTimeOffset>(8)
                )
            );
        }
        return result;
    }

    /// <summary>
    /// Targeted fetch of just the (potentially multi-MB) `serialized_game` blob, for a specific and
    /// deliberately small set of game ids - used by ImportGamesAsync so the wide, keyset-paged read
    /// above never has to transfer this column at all for games whose blob is already stored
    /// locally (assumed immutable once captured - true for Finished/Cancelled games in general, and
    /// always true for the one-off final cutover import, which only ever runs after the production
    /// site has been stopped). Returns null for a game whose serialized_game is itself legitimately
    /// null in the legacy database (as opposed to a game id that doesn't exist at all, which simply
    /// won't appear as a key in the result).
    /// </summary>
    public async Task<Dictionary<Guid, string?>> ReadSerializedGamesByIdsAsync(
        IReadOnlyCollection<Guid> ids
    )
    {
        var result = new Dictionary<Guid, string?>();
        if (ids.Count == 0)
        {
            return result;
        }
        await using var conn = OpenConnectionWithTimeout(120);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, serialized_game::text
            FROM agotboardgame_main_game
            WHERE id = ANY(@ids)
            """,
            conn
        );
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        return result;
    }

    public async IAsyncEnumerable<LegacyPlayerInGame> ReadPlayersInGameAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT game_id, user_id, data::text
            FROM agotboardgame_main_playeringame
            ORDER BY game_id
            """,
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyPlayerInGame(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2)
            );
        }
    }

    /// <summary>
    /// Chat history can be very large (potentially years of messages across tens of thousands of
    /// rooms) - paged for the same reason as <see cref="ReadGamesAsync"/>. Django's default
    /// auto-incrementing `id` on `chat_message` doubles as the keyset tiebreaker here AND is
    /// carried through onto <see cref="LegacyMessage"/> itself, since Message.Id now preserves it
    /// exactly (see LegacyMessage's doc comment) rather than generating a fresh id on import.
    /// </summary>
    public async IAsyncEnumerable<LegacyMessage> ReadMessagesAsync(DateTimeOffset? sinceUtc = null)
    {
        const int pageSize = 2000;
        DateTimeOffset? afterCreatedAt = null;
        long? afterId = null;
        while (true)
        {
            var page = await ReadMessagesPageAsync(sinceUtc, afterCreatedAt, afterId, pageSize);
            if (page.Count == 0)
            {
                yield break;
            }
            foreach (var message in page)
            {
                yield return message;
                afterCreatedAt = message.CreatedAt;
                afterId = message.Id;
            }
        }
    }

    private async Task<List<LegacyMessage>> ReadMessagesPageAsync(
        DateTimeOffset? sinceUtc,
        DateTimeOffset? afterCreatedAt,
        long? afterId,
        int pageSize
    )
    {
        await using var conn = OpenConnection();
        var sql = afterCreatedAt is null
            ? sinceUtc is null
                ? "SELECT id, room_id, user_id, text, created_at FROM chat_message ORDER BY created_at, id LIMIT @pageSize"
                : "SELECT id, room_id, user_id, text, created_at FROM chat_message WHERE created_at >= @since ORDER BY created_at, id LIMIT @pageSize"
            : sinceUtc is null
                ? "SELECT id, room_id, user_id, text, created_at FROM chat_message WHERE (created_at, id) > (@afterCreatedAt, @afterId) ORDER BY created_at, id LIMIT @pageSize"
                : "SELECT id, room_id, user_id, text, created_at FROM chat_message WHERE created_at >= @since AND (created_at, id) > (@afterCreatedAt, @afterId) ORDER BY created_at, id LIMIT @pageSize";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pageSize", pageSize);
        if (sinceUtc is { } since)
        {
            cmd.Parameters.AddWithValue("since", since);
        }
        if (afterCreatedAt is { } createdAt)
        {
            cmd.Parameters.AddWithValue("afterCreatedAt", createdAt);
            cmd.Parameters.AddWithValue("afterId", afterId!.Value);
        }

        var result = new List<LegacyMessage>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(
                new LegacyMessage(
                    reader.GetInt64(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)
                )
            );
        }
        return result;
    }

    public async IAsyncEnumerable<LegacyPbemResponseTime> ReadPbemResponseTimesAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, user_id, response_time, created_at
            FROM agotboardgame_main_pbemresponsetime
            ORDER BY created_at
            """,
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyPbemResponseTime(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(3)
            );
        }
    }

    public async Task<List<Guid>> SampleIdsAsync(string table, int count)
    {
        var result = new List<Guid>();
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            $"SELECT id FROM {table} ORDER BY random() LIMIT @count",
            conn
        );
        cmd.Parameters.AddWithValue("count", count);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetGuid(0));
        }
        return result;
    }

    /// <summary>
    /// Upfront row counts for the two tables slow enough that a progress report needs "N of M" to
    /// be useful (chat_message can hold millions of rows, agotboardgame_main_game's SerializedGame
    /// blobs make each row itself slow to transfer) - see ImportMessagesAsync/ImportGamesAsync.
    /// </summary>
    public async Task<long> CountMessagesAsync(DateTimeOffset? sinceUtc = null)
    {
        await using var conn = OpenConnection();
        var sql = sinceUtc is null
            ? "SELECT COUNT(*) FROM chat_message"
            : "SELECT COUNT(*) FROM chat_message WHERE created_at >= @since";
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (sinceUtc is { } since)
        {
            cmd.Parameters.AddWithValue("since", since);
        }
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<long> CountGamesAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM agotboardgame_main_game",
            conn
        );
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<Dictionary<string, long>> ReadRowCountsAsync()
    {
        var tables = new[]
        {
            "agotboardgame_main_user",
            "auth_group",
            "chat_room",
            "agotboardgame_main_game",
            "agotboardgame_main_playeringame",
            "chat_message",
            "agotboardgame_main_pbemresponsetime",
        };
        var result = new Dictionary<string, long>();
        await using var conn = OpenConnection();
        foreach (var table in tables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table}", conn);
            result[table] = (long)(await cmd.ExecuteScalarAsync())!;
        }
        return result;
    }
}
