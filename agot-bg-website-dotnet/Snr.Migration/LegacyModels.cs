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
/// chat_userinroom (chat/models.py's UserInRoom) — membership of a user in a room. Only the
/// (user_id, room_id) pair is carried over: `last_viewed_message_id` references the legacy
/// integer chat_message.id, which has no counterpart in the new Guid-keyed Message table (see
/// LegacyMessage's doc comment / MIGRATION_PLAN.md §4.1 — messages get fresh ids on import), so it
/// isn't worth threading through just to prefill an "unread" marker.
/// </summary>
public record LegacyUserInRoom(Guid UserId, Guid RoomId);

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

public record LegacyMessage(Guid RoomId, Guid UserId, string Text, DateTimeOffset CreatedAt);

public record LegacyPbemResponseTime(
    Guid Id,
    Guid UserId,
    int ResponseTime,
    DateTimeOffset CreatedAt
);
