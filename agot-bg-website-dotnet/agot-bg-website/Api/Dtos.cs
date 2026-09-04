using System.Text.Json;
using System.Text.Json.Serialization;

namespace agot_bg_website.Api;

// DTOs mirroring the JSON field names of the current Django REST contract exactly (snake_case via
// JsonSerializerOptions.PropertyNamingPolicy configured in Program.cs), so
// WebsiteClient.ts/LiveWebsiteClient.ts need no changes — see MIGRATION_PLAN.md §6.

public record UserDto(
    Guid Id,
    string Username,
    string GameToken,
    bool IsStaff,
    bool MuteGames,
    bool UseHouseNamesForChat,
    bool UseMapScrollbar,
    // Renamed to GameStateColumnRight on ApplicationUser (see its doc comment) - the game server's
    // wire contract still expects the legacy `use_responsive_layout_on_mobile` JSON key, so pin it
    // explicitly rather than let it follow the new CLR/property name.
    [property: JsonPropertyName("use_responsive_layout_on_mobile")] bool GameStateColumnRight,
    IReadOnlyList<string> Groups
);

public record PlayerInGamePatchDto(Guid User, JsonElement Data);

public record GamePatchDto(
    JsonElement? SerializedGame,
    string? State,
    string? Version,
    JsonElement? ViewOfGame,
    IReadOnlyList<PlayerInGamePatchDto>? Players,
    bool? UpdateLastActive
);

public record GameDto(
    Guid Id,
    string Name,
    Guid Owner,
    JsonElement? SerializedGame,
    string? Version,
    string State,
    JsonElement? ViewOfGame
);

public record CreateRoomDto(
    string Name,
    bool Public,
    IReadOnlyList<UserInRoomDto> Users,
    int? MaxRetrieveCount
);

/// <summary>
/// One entry of CreateRoomDto.Users — matches Django's UserInRoomSerializer wire shape
/// (`[{"user": "<id>"}, ...]`), which is what LiveWebsiteClient.ts's createPrivateChatRoom/
/// createPublicChatRoom actually send (`users.map(u => ({user: u.id}))`), not a plain array of
/// GUIDs. Mirrors PlayerInGamePatchDto's identical User-wrapper pattern above.
/// </summary>
public record UserInRoomDto(Guid User);

public record RoomDto(Guid Id, string Name, bool Public);
