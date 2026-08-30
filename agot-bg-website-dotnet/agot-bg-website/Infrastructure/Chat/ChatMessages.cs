using System.Text.Json.Serialization;

namespace agot_bg_website.Infrastructure.Chat;

/// <summary>
/// Wire DTOs for the raw WebSocket JSON protocol spoken by <c>ChatClient.ts</c> and the website's
/// preact chat widgets (games_chat.html/dual_chat.html) — property names are exactly the
/// snake_case names the existing JS already sends/expects, so none of that client code needs to
/// change. See MIGRATION_PLAN.md §7 and chat/consumers.py.
/// </summary>
public sealed record ChatMessageEvent
{
    [JsonPropertyName("type")]
    public string Type => "chat_message";

    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("user_id")]
    public required Guid UserId { get; init; }

    [JsonPropertyName("user_username")]
    public required string UserUsername { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record MessagesRetrievedEvent
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "chat_messages_retrieved" | "more_chat_messages_retrieved"

    [JsonPropertyName("messages")]
    public required List<MessageData> Messages { get; init; }

    [JsonPropertyName("last_viewed_message")]
    public Guid? LastViewedMessage { get; init; }
}

public sealed record MessageData
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("user_id")]
    public required Guid UserId { get; init; }

    [JsonPropertyName("user_username")]
    public required string UserUsername { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record ConnectedUsersEvent
{
    [JsonPropertyName("type")]
    public string Type => "connected_users";

    [JsonPropertyName("users")]
    public required Dictionary<string, ConnectedUserWireData> Users { get; init; }
}

/// <summary>Public shape sent to clients — strips the internal Count/LastActiveAt bookkeeping fields.</summary>
public sealed record ConnectedUserWireData
{
    [JsonPropertyName("username")]
    public required string Username { get; init; }

    [JsonPropertyName("is_admin")]
    public required bool IsAdmin { get; init; }

    [JsonPropertyName("is_high_member")]
    public required bool IsHighMember { get; init; }

    [JsonPropertyName("last_won_tournament")]
    public string? LastWonTournament { get; init; }
}

public sealed record ForceDisconnectEvent
{
    [JsonPropertyName("type")]
    public string Type => "force_disconnect";
}

/// <summary>
/// Internal-only pub/sub payload (never forwarded verbatim to browsers) used to tell every
/// instance which locally-connected users, if any, were pruned as stale from the public room's
/// presence list — each instance then sends a personalized <see cref="ForceDisconnectEvent"/> only
/// to the matching local socket(s), mirroring Django's per-consumer <c>close_stale_connections</c>.
/// </summary>
public sealed record PruneCheckEvent
{
    [JsonPropertyName("type")]
    public string Type => "__prune_check__";

    [JsonPropertyName("user_ids")]
    public required List<Guid> UserIds { get; init; }
}
