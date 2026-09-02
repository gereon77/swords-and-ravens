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

    public async IAsyncEnumerable<LegacyGame> ReadGamesAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, name, owner_id, view_of_game::text, serialized_game::text, version, state,
                   created_at, updated_at, last_active_at
            FROM agotboardgame_main_game
            ORDER BY created_at
            """,
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyGame(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9)
            );
        }
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

    public async IAsyncEnumerable<LegacyMessage> ReadMessagesAsync(DateTimeOffset? sinceUtc = null)
    {
        await using var conn = OpenConnection();
        var sql = sinceUtc is null
            ? "SELECT room_id, user_id, text, created_at FROM chat_message ORDER BY created_at"
            : "SELECT room_id, user_id, text, created_at FROM chat_message WHERE created_at >= @since ORDER BY created_at";
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (sinceUtc is { } since)
        {
            cmd.Parameters.AddWithValue("since", since);
        }
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return new LegacyMessage(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)
            );
        }
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
