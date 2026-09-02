using System.Text.Json;

namespace agot_bg_website.Domain;

public enum GameState
{
    InLobby,
    Ongoing,
    Finished,
    Closed,
    Cancelled,
}

/// <summary>
/// A hosted game. SerializedGame/ViewOfGame remain opaque JSON blobs owned by the TS game
/// server — see MIGRATION_PLAN.md §4.2/§4.4. Two small exceptions read a couple of top-level
/// fields directly: <c>GamesApi.cs</c>'s PATCH handler reads <c>view_of_game.turn</c>/
/// <c>publicChatRoomId</c> to delete games cancelled before ever leaving the lobby, and
/// <c>Snr.Migration</c> reads <c>childGameState.oldPlayerIds</c>/<c>timeoutPlayerIds</c> to
/// backfill historical <see cref="PreviousPlayerInGame"/> rows for legacy games (see
/// MIGRATION_PLAN.md §10.1). Neither reconstructs the full game state.
/// </summary>
public class Game
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public Guid OwnerUserId { get; set; }

    public ApplicationUser? OwnerUser { get; set; }

    public JsonDocument? SerializedGame { get; set; }

    public JsonDocument? ViewOfGame { get; set; }

    public string? Version { get; set; }

    public GameState State { get; set; } = GameState.InLobby;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PlayerInGame> Players { get; set; } = [];

    public List<PreviousPlayerInGame> PreviousPlayers { get; set; } = [];
}

/// <summary>Current players in a game. Fully replaced on every save, mirroring Django's behavior.</summary>
public class PlayerInGame
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public Game? Game { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public JsonDocument? Data { get; set; }
}

/// <summary>Reason a player stopped holding a house partway through a game. See MIGRATION_PLAN.md §4.4.</summary>
public enum PlayerReplacementReason
{
    Vote,
    ClockTimeout,
}

/// <summary>
/// A player who was removed from a game before it ended (replaced by a vassal via vote, or timed
/// out). Does not exist in Django today — see MIGRATION_PLAN.md §4.4. Computed entirely by this
/// app itself (never sent by the game server, which only ever sends the current `Players` list on
/// every save) - see GamesApi.cs's PATCH handler: whenever a previously-present user is missing
/// from a save's player list, a row is added here; if a user with an existing row reappears in a
/// later save (voted back in), the row is removed again. At most one row per (GameId, UserId) can
/// exist at a time.
///
/// <see cref="Reason"/> is nullable: the live save-game endpoint has no way to distinguish *why* a
/// player left from the Players list alone, so it leaves this null. Only the historical import
/// backfill (Snr.Migration, reading `oldPlayerIds`/`timeoutPlayerIds` off the full serialized
/// game) can set it directly. A future cron job could fill in Reason for live-added rows the same
/// way, by re-reading the game's SerializedGame after the fact.
/// </summary>
public class PreviousPlayerInGame
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public Game? Game { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public PlayerReplacementReason? Reason { get; set; }

    public DateTimeOffset? ReplacedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PbemResponseTime
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public int ResponseTime { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Room
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public bool Public { get; set; }

    public int? MaxRetrieveCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Message> Messages { get; set; } = [];

    public List<UserInRoom> UsersInRoom { get; set; } = [];
}

public class Message
{
    public Guid Id { get; set; }

    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public required string Text { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class UserInRoom
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    public Guid? LastViewedMessageId { get; set; }
}
