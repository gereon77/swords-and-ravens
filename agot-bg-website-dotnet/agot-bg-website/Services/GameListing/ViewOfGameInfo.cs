using System.Text.Json;

namespace agot_bg_website.Services.GameListing;

/// <summary>
/// Typed view over the handful of `Game.ViewOfGame` JSON fields the games lists need, mirroring
/// what Django's games/games_table templates read directly off `view_of_game` (turn, waitingFor,
/// settings.pbem, settings.private, ...) — see EntireGame.ts's getViewOfGame() in the game server
/// for the authoritative shape. Never touches SerializedGame.
/// </summary>
public sealed record ViewOfGameInfo(
    int? Turn,
    string? WaitingFor,
    IReadOnlySet<Guid> WaitingForIds,
    int? MaxPlayerCount,
    bool IsPbem,
    bool IsFaceless,
    bool IsPrivate,
    bool IsPasswordProtected,
    bool IsTournamentMode,
    bool ReplacePlayerVoteOngoing,
    Guid? PublicChatRoomId,
    bool IsLearnTheGame,
    string? SetupId,
    IReadOnlySet<Guid> ReplacerIds,
    IReadOnlySet<Guid> InitialPlayerIds
)
{
    public static readonly ViewOfGameInfo Empty = new(
        null,
        null,
        new HashSet<Guid>(),
        null,
        false,
        false,
        false,
        false,
        false,
        false,
        null,
        false,
        null,
        new HashSet<Guid>(),
        new HashSet<Guid>()
    );

    public static ViewOfGameInfo Parse(JsonDocument? viewOfGame)
    {
        if (viewOfGame is null)
        {
            return Empty;
        }

        var root = viewOfGame.RootElement;

        var turn =
            root.TryGetProperty("turn", out var turnEl) && turnEl.ValueKind == JsonValueKind.Number
                ? turnEl.GetInt32()
                : (int?)null;

        var waitingFor =
            root.TryGetProperty("waitingFor", out var wfEl)
            && wfEl.ValueKind == JsonValueKind.String
                ? wfEl.GetString()
                : null;

        var waitingForIds = ReadGuidSet(root, "waitingForIds");

        // Populated by the game server (`IngameGameState.replacerIds`, via `VoteType.ts`'s
        // player-replacement handling) with the user id of whoever seated into an existing house
        // through a replacement vote - see MIGRATION_PLAN.md §10.2's win-rate addendum. Present in
        // `ViewOfGame` since the same commit that added `oldPlayerIds`/`timeoutPlayerIds` (which
        // `PreviousPlayersBackfill`/`PreviousPlayerReasonResolver` already rely on being reliably
        // populated for any historical game that ever reached the ingame state), so no separate
        // historical backfill is needed here either - re-running `UserStatsService.RecalculateAsync`
        // is enough to pick this up for already-played games.
        var replacerIds = ReadGuidSet(root, "replacerIds");

        // The user ids seated when the game began (game server's IngameGameState.initialPlayerIds,
        // derived once from the one-time "user-house-assignments" log entry - see
        // MIGRATION_PLAN.md §14). Used to refine the ReplacerIds win-rate exemption: a user who was
        // ALSO an original player of this game (even if they additionally replaced into another
        // house later) should not get the "pure replacer" exemption for their own removal(s).
        // Unlike ReplacerIds, this field didn't exist for older games, so - unless the game has
        // been loaded by the game server since (which self-heals it via serializedGameMigrations.ts
        // version "137") - historical games need the one-time `InitialPlayersBackfill` tool in
        // Snr.Migration to populate it before `UserStatsService.RecalculateAsync` reflects it.
        var initialPlayerIds = ReadGuidSet(root, "initialPlayerIds");

        var maxPlayerCount =
            root.TryGetProperty("maxPlayerCount", out var maxEl)
            && maxEl.ValueKind == JsonValueKind.Number
                ? maxEl.GetInt32()
                : (int?)null;

        var isPasswordProtected =
            root.TryGetProperty("isPasswordProtected", out var pwEl)
            && pwEl.ValueKind == JsonValueKind.True;

        var replacePlayerVoteOngoing =
            root.TryGetProperty("replacePlayerVoteOngoing", out var voteEl)
            && voteEl.ValueKind == JsonValueKind.True;

        Guid? publicChatRoomId =
            root.TryGetProperty("publicChatRoomId", out var roomEl)
            && roomEl.ValueKind == JsonValueKind.String
            && Guid.TryParse(roomEl.GetString(), out var roomId)
                ? roomId
                : null;

        var settings =
            root.TryGetProperty("settings", out var settingsEl)
            && settingsEl.ValueKind == JsonValueKind.Object
                ? settingsEl
                : (JsonElement?)null;

        bool GetBoolSetting(string name) =>
            settings?.TryGetProperty(name, out var el) == true
            && el.ValueKind == JsonValueKind.True;

        // The tutorial variant is excluded from win-rate stats entirely (MIGRATION_PLAN.md §10.2)
        // and, on the games lists, from the "faceless" filtering below - it's identified by this
        // one magic setupId rather than a dedicated boolean setting.
        var setupId =
            settings?.TryGetProperty("setupId", out var setupIdEl) == true
            && setupIdEl.ValueKind == JsonValueKind.String
                ? setupIdEl.GetString()
                : null;
        var isLearnTheGame = setupId == "learn-the-game";

        return new ViewOfGameInfo(
            turn,
            waitingFor,
            waitingForIds,
            maxPlayerCount,
            GetBoolSetting("pbem"),
            GetBoolSetting("faceless"),
            GetBoolSetting("private"),
            isPasswordProtected,
            GetBoolSetting("tournamentMode"),
            replacePlayerVoteOngoing,
            publicChatRoomId,
            isLearnTheGame,
            setupId,
            replacerIds,
            initialPlayerIds
        );
    }

    /// <summary>
    /// True only for a "pure" replacer: someone who joined this specific game solely by
    /// replacing an existing player, and was never one of the original players themselves (even
    /// if they later also replaced into a different house). Used everywhere the "no downside
    /// risk for helping finish a stalling game" exemption is applied - a user's own removal(s)
    /// from a game they originally started in should still count normally.
    /// </summary>
    public bool IsPureReplacer(Guid userId) =>
        ReplacerIds.Contains(userId) && !InitialPlayerIds.Contains(userId);

    private static HashSet<Guid> ReadGuidSet(JsonElement root, string propertyName)
    {
        var result = new HashSet<Guid>();
        if (
            root.TryGetProperty(propertyName, out var arrEl)
            && arrEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var idEl in arrEl.EnumerateArray())
            {
                if (
                    idEl.ValueKind == JsonValueKind.String
                    && Guid.TryParse(idEl.GetString(), out var id)
                )
                {
                    result.Add(id);
                }
            }
        }
        return result;
    }
}

/// <summary>Typed view over a single `PlayerInGame.Data` JSON blob, from EntireGame.ts's getPlayersInGame().</summary>
public sealed record PlayerInGameInfo(
    string? House,
    bool WaitedFor,
    bool NeededForVote,
    IReadOnlyList<Guid> ImportantChatRoomIds,
    bool? IsWinner
)
{
    public static readonly PlayerInGameInfo Empty = new(null, false, false, [], null);

    public static PlayerInGameInfo Parse(JsonDocument? data)
    {
        if (data is null)
        {
            return Empty;
        }

        var root = data.RootElement;

        var house =
            root.TryGetProperty("house", out var houseEl)
            && houseEl.ValueKind == JsonValueKind.String
                ? houseEl.GetString()
                : null;

        var waitedFor =
            root.TryGetProperty("waited_for", out var waitedEl)
            && waitedEl.ValueKind == JsonValueKind.True;
        var neededForVote =
            root.TryGetProperty("needed_for_vote", out var voteEl)
            && voteEl.ValueKind == JsonValueKind.True;

        bool? isWinner =
            root.TryGetProperty("is_winner", out var isWinnerEl)
            && (
                isWinnerEl.ValueKind == JsonValueKind.True
                || isWinnerEl.ValueKind == JsonValueKind.False
            )
                ? isWinnerEl.GetBoolean()
                : null;

        var importantChatRoomIds = new List<Guid>();
        if (
            root.TryGetProperty("important_chat_rooms", out var roomsEl)
            && roomsEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var idEl in roomsEl.EnumerateArray())
            {
                if (
                    idEl.ValueKind == JsonValueKind.String
                    && Guid.TryParse(idEl.GetString(), out var id)
                )
                {
                    importantChatRoomIds.Add(id);
                }
            }
        }

        return new PlayerInGameInfo(
            house,
            waitedFor,
            neededForVote,
            importantChatRoomIds,
            isWinner
        );
    }
}
