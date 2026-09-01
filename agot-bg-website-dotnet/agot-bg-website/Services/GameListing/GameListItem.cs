namespace agot_bg_website.Services.GameListing;

/// <summary>
/// One row of any of the games lists (open/ongoing/my games/inactive.../replacement needed) —
/// the ASP.NET Core equivalent of the context Django's games_table.html template renders per
/// `game`, but pre-computed in C# instead of doing JSON lookups inside the template.
/// </summary>
public sealed record GameListItem(
    Guid Id,
    string Name,
    Domain.GameState State,
    Guid OwnerUserId,
    string? OwnerDisplayName,
    int PlayersCount,
    int? MaxPlayerCount,
    bool IsPbem,
    bool IsPasswordProtected,
    bool IsPrivate,
    bool IsFaceless,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    int? Turn,
    string? WaitingFor,
    // Personalization for the current viewer, null/false if not authenticated or not a player.
    string? MyHouse,
    bool MyTurn,
    bool MyNeededForVote,
    bool UnreadPublicMessages,
    bool UnreadPrivateMessages,
    // "Replacement needed for: Stark (username), ..." - null unless there's an inactive waited-for player.
    string? ReplacementNeededFor,
    // First inactive waited-for player's user id, for the admin-only "join as ..." action.
    Guid? JoinAsUserId);
