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
    string? SetupId
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
        null
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

        var waitingForIds = new HashSet<Guid>();
        if (
            root.TryGetProperty("waitingForIds", out var wfIdsEl)
            && wfIdsEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var idEl in wfIdsEl.EnumerateArray())
            {
                if (
                    idEl.ValueKind == JsonValueKind.String
                    && Guid.TryParse(idEl.GetString(), out var id)
                )
                {
                    waitingForIds.Add(id);
                }
            }
        }

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
            setupId
        );
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

        return new PlayerInGameInfo(house, waitedFor, neededForVote, importantChatRoomIds, isWinner);
    }
}
