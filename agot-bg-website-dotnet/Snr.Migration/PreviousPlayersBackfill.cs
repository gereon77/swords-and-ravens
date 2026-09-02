using System.Text.Json;
using agot_bg_website.Domain;

namespace Snr.Migration;

/// <summary>
/// Historical backfill of <see cref="PreviousPlayerInGame"/> rows for legacy games, computed
/// directly from the game's full <c>SerializedGame</c> JSON (never <c>ViewOfGame</c> — older
/// <c>ViewOfGame</c> blobs may predate the introduction of <c>oldPlayerIds</c>/<c>timeoutPlayerIds</c>,
/// while <c>SerializedGame</c> — the game server's own internal state — always has them for any
/// game that reached the ingame state). See MIGRATION_PLAN.md §10.1.
///
/// Reads the shape produced by agot-bg-game-server's IngameGameState.ts:
/// - <c>childGameState.type == "ingame"</c>, with sibling <c>players</c> (still-current, keyed by
///   <c>userId</c>), <c>oldPlayerIds</c> (removed by vote) and <c>timeoutPlayerIds</c> (removed by
///   clock timeout) — confirmed mutually exclusive by construction (IngameGameState.ts's
///   replacePlayerByVassal only ever pushes to one or the other, never both, depending on
///   ReplacementReason).
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
    public static List<PreviousPlayerInGame> Compute(Guid gameId, JsonDocument? serializedGame)
    {
        var result = new List<PreviousPlayerInGame>();
        if (serializedGame is null)
        {
            return result;
        }

        var root = serializedGame.RootElement;
        if (
            !root.TryGetProperty("childGameState", out var ingame)
            || ingame.ValueKind != JsonValueKind.Object
            || !ingame.TryGetProperty("type", out var typeEl)
            || typeEl.ValueKind != JsonValueKind.String
            || typeEl.GetString() != "ingame"
        )
        {
            // Game never reached the ingame state (still in lobby, or was cancelled before
            // drafting finished) - nothing to backfill.
            return result;
        }

        var currentPlayerIds = new HashSet<string>();
        if (
            ingame.TryGetProperty("players", out var playersEl)
            && playersEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var p in playersEl.EnumerateArray())
            {
                if (
                    p.TryGetProperty("userId", out var uidEl)
                    && uidEl.ValueKind == JsonValueKind.String
                    && uidEl.GetString() is { } uid
                )
                {
                    currentPlayerIds.Add(uid);
                }
            }
        }

        var oldPlayerIds = ReadStringArray(ingame, "oldPlayerIds");
        var timeoutPlayerIds = ReadStringArray(ingame, "timeoutPlayerIds");

        foreach (var userId in oldPlayerIds.Distinct())
        {
            AddIfRemoved(result, gameId, userId, currentPlayerIds, PlayerReplacementReason.Vote);
        }

        foreach (var userId in timeoutPlayerIds.Distinct())
        {
            AddIfRemoved(
                result,
                gameId,
                userId,
                currentPlayerIds,
                PlayerReplacementReason.ClockTimeout
            );
        }

        return result;
    }

    private static void AddIfRemoved(
        List<PreviousPlayerInGame> result,
        Guid gameId,
        string userId,
        HashSet<string> currentPlayerIds,
        PlayerReplacementReason reason
    )
    {
        if (currentPlayerIds.Contains(userId))
        {
            return; // voted back in (or never actually left, for a malformed/duplicate entry)
        }
        if (!Guid.TryParse(userId, out var userGuid))
        {
            return; // defensive: malformed/unparseable id, skip rather than throw
        }
        if (result.Any(r => r.UserId == userGuid))
        {
            return; // oldPlayerIds/timeoutPlayerIds are disjoint by construction, but guard anyway
        }

        result.Add(
            new PreviousPlayerInGame
            {
                Id = Guid.NewGuid(),
                GameId = gameId,
                UserId = userGuid,
                Reason = reason,
                ReplacedAt = null, // not derivable without replaying the votes log - see class doc comment
            }
        );
    }

    private static List<string> ReadStringArray(JsonElement obj, string propertyName)
    {
        var list = new List<string>();
        if (
            obj.TryGetProperty(propertyName, out var arrEl)
            && arrEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var el in arrEl.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                {
                    list.Add(s);
                }
            }
        }
        return list;
    }
}
