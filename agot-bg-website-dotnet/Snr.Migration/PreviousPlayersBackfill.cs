using System.Text.Json;
using agot_bg_website.Domain;

namespace Snr.Migration;

/// <summary>
/// Historical backfill of <see cref="PreviousPlayerInGame"/> rows for legacy games, computed from
/// the game's <c>ViewOfGame</c> JSON (NOT the much larger <c>SerializedGame</c>) plus that game's
/// current player membership (sourced separately from the legacy <c>agotboardgame_main_playeringame</c>
/// table, since <c>ViewOfGame</c> itself has no raw list of current player user-ids - only
/// <c>victoryTrack[].player</c>, a display username string, not a userId).
///
/// <c>ViewOfGame</c> is a flat summary object (see EntireGame.getViewOfGame()) - unlike
/// <c>SerializedGame</c>, it has no <c>childGameState</c> nesting. Its top-level
/// <c>oldPlayerIds</c>/<c>timeoutPlayerIds</c> string arrays mirror
/// <c>ingameGameState.oldPlayerIds</c>/<c>timeoutPlayerIds</c> directly and have been present
/// unconditionally since the feature was introduced, for any game that ever reached the ingame
/// state - confirmed mutually exclusive by construction. Reason resolution itself is shared with
/// the live save-game endpoint via <see cref="PreviousPlayerReasonResolver"/>.
///
/// This deliberately does not attempt to resolve which House a removed player held, whether they
/// won, or a precise removal order/timestamp: reconstructing that reliably from `votes` would
/// still miss cases (e.g. a "replace-vassal-by-player" swap back, or a user who held more than one
/// house across replacements) and isn't needed - every PreviousPlayerInGame row counts as a loss
/// unconditionally, regardless of any of that (see MIGRATION_PLAN.md §10.2).
///
/// This is a best-effort, one-off gap-filler only. Any future real save of the same game by the
/// live game server may add/remove rows for that game (see GamesApi.cs's PATCH handler), so
/// authoritative data always supersedes anything backfilled here. Callers must therefore only call
/// <see cref="Compute"/> for games that don't already have any PreviousPlayerInGame rows.
/// </summary>
internal static class PreviousPlayersBackfill
{
    public static List<PreviousPlayerInGame> Compute(
        Guid gameId,
        JsonDocument? viewOfGame,
        HashSet<Guid> currentPlayerUserIds
    )
    {
        var result = new List<PreviousPlayerInGame>();
        if (viewOfGame is null)
        {
            return result;
        }

        var root = viewOfGame.RootElement;
        var oldPlayerIds = ReadGuidArray(root, "oldPlayerIds");
        var timeoutPlayerIds = ReadGuidArray(root, "timeoutPlayerIds");

        foreach (var userId in oldPlayerIds.Distinct())
        {
            AddIfRemoved(result, gameId, userId, currentPlayerUserIds, viewOfGame);
        }

        foreach (var userId in timeoutPlayerIds.Distinct())
        {
            AddIfRemoved(result, gameId, userId, currentPlayerUserIds, viewOfGame);
        }

        return result;
    }

    private static void AddIfRemoved(
        List<PreviousPlayerInGame> result,
        Guid gameId,
        Guid userId,
        HashSet<Guid> currentPlayerUserIds,
        JsonDocument viewOfGame
    )
    {
        if (currentPlayerUserIds.Contains(userId))
        {
            return; // voted back in (or never actually left, for a malformed/duplicate entry)
        }
        if (result.Any(r => r.UserId == userId))
        {
            return; // oldPlayerIds/timeoutPlayerIds are disjoint by construction, but guard anyway
        }

        result.Add(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = gameId,
                UserId = userId,
                Reason = PreviousPlayerReasonResolver.Resolve(viewOfGame, userId),
                ReplacedAt = null, // not derivable without replaying the votes log - see class doc comment
            }
        );
    }

    private static List<Guid> ReadGuidArray(JsonElement obj, string propertyName)
    {
        var list = new List<Guid>();
        if (
            obj.TryGetProperty(propertyName, out var arrEl)
            && arrEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var el in arrEl.EnumerateArray())
            {
                if (
                    el.ValueKind == JsonValueKind.String
                    && el.GetString() is { } s
                    && Guid.TryParse(s, out var guid)
                )
                {
                    list.Add(guid);
                }
            }
        }
        return list;
    }
}
