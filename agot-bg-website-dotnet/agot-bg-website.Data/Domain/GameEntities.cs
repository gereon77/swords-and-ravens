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
/// server — see MIGRATION_PLAN.md §4.2/§4.4. This app never parses their contents except
/// for the one-off historical backfill script, which lives in agot-bg-game-server, not here.
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
    ReplacedByPlayer,
}

/// <summary>
/// A player who was removed from a game before it ended (replaced by a vassal, by another human
/// player, or timed out). Does not exist in Django today — see MIGRATION_PLAN.md §4.4. These rows
/// are fully replaced on every save, same idempotency pattern as PlayerInGame.
/// </summary>
public class PreviousPlayerInGame
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public Game? Game { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public required string House { get; set; }

    /// <summary>0-based order of removal within the game; natural key alongside GameId.</summary>
    public int SequenceNumber { get; set; }

    public PlayerReplacementReason Reason { get; set; }

    /// <summary>Whether House ultimately won. Null while the game is still ongoing. Not used for win-rate.</summary>
    public bool? WasWinner { get; set; }

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
