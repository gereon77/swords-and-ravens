namespace Snr.Migration;

// Plain rows read from the legacy Django Postgres schema (agot-bg-website/agotboardgame_main,
// agot-bg-website/chat). Table/column names below mirror Django's default naming
// (<app_label>_<model_name_lower>), confirmed against agotboardgame_main/models.py and
// chat/models.py — see MIGRATION_PLAN.md §10.

public record LegacyUser(
    Guid Id,
    string Username,
    string? Email,
    string GameToken,
    string? ProfileText,
    string? LastWonTournament,
    bool EmailNotificationActive,
    bool MuteGames,
    bool UseHouseNamesForChat,
    bool UseMapScrollbar,
    bool UseResponsiveLayoutOnMobile,
    DateTimeOffset? LastUsernameUpdateTime,
    DateTimeOffset LastActivity,
    int VanillaForumUserId,
    DateTimeOffset DateJoined
);

public record LegacyGroup(int Id, string Name);

public record LegacyUserGroup(Guid UserId, int GroupId);

public record LegacyRoom(
    Guid Id,
    string Name,
    bool Public,
    int? MaxRetrieveCount,
    DateTimeOffset CreatedAt
);

/// <summary>
/// chat_userinroom (chat/models.py's UserInRoom) — membership of a user in a room, plus which
/// message (if any) this user last viewed in it. last_viewed_message_id carries over directly:
/// unlike every other chat_message reference in this migration, Message.Id itself now preserves
/// chat_message.id exactly (see LegacyMessage's doc comment), so no id-resolution step is needed.
/// </summary>
public record LegacyUserInRoom(Guid UserId, Guid RoomId, long? LastViewedMessageId);

public record LegacyGame(
    Guid Id,
    string Name,
    Guid OwnerId,
    string? ViewOfGame,
    string? Version,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastActiveAt
);

public record LegacyPlayerInGame(Guid GameId, Guid UserId, string Data);

/// <summary>
/// chat_message (chat/models.py's Message). Id is carried over exactly (a plain Django AutoField,
/// confirmed via information_schema to be a 32-bit int in the legacy database, widened to long on
/// the target side for headroom - see ApplicationDbContext's Message entity config), unlike every
/// other table in this file's records where the target uses a freshly generated Guid.
/// </summary>
public record LegacyMessage(
    long Id,
    Guid RoomId,
    Guid UserId,
    string Text,
    DateTimeOffset CreatedAt
);

public record LegacyPbemResponseTime(
    Guid Id,
    Guid UserId,
    int ResponseTime,
    DateTimeOffset CreatedAt
);
