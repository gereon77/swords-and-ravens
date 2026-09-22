using System.Text.Json;

namespace agot_bg_website.Domain;

/// <summary>
/// Resolves why a player was removed from a game (vote vs. clock timeout) by reading a game's
/// <c>ViewOfGame</c> JSON - NOT the much larger <c>SerializedGame</c>. <c>ViewOfGame</c> is a flat
/// summary object (see agot-bg-game-server's EntireGame.getViewOfGame()); its top-level
/// <c>oldPlayerIds</c>/<c>timeoutPlayerIds</c> string arrays mirror
/// <c>ingameGameState.oldPlayerIds</c>/<c>timeoutPlayerIds</c> directly and have been present
/// unconditionally (only omitted/empty, never differently-shaped) since the feature was
/// introduced, for any game that ever reached the ingame state - they are mutually exclusive by
/// construction (IngameGameState.ts's replacement logic only ever pushes a removed user's id to
/// one or the other).
///
/// Used both by the live save-game endpoint (GamesApi.cs's PATCH handler, called with the just-saved
/// <c>Game.ViewOfGame</c>) and by Snr.Migration's historical backfill (PreviousPlayersBackfill.cs,
/// called with the legacy game's <c>view_of_game</c>) so both paths agree on the same logic.
/// </summary>
public static class PreviousPlayerReasonResolver
{
    /// <summary>
    /// Returns the reason a removed player left, or null if <paramref name="viewOfGame"/> is
    /// missing, the game never reached the ingame state, or <paramref name="userId"/> doesn't
    /// appear in either array - e.g. a replace-player-by-player/vassal swap this data model
    /// otherwise doesn't track (see PreviousPlayerInGame's doc comment). Callers must leave
    /// <see cref="PlayerReplacementReason"/>? null in that case rather than guessing.
    /// </summary>
    public static PlayerReplacementReason? Resolve(JsonDocument? viewOfGame, Guid userId)
    {
        if (viewOfGame is null)
        {
            return null;
        }

        var root = viewOfGame.RootElement;
        var idStr = userId.ToString();

        if (ContainsId(root, "oldPlayerIds", idStr))
        {
            return PlayerReplacementReason.Vote;
        }

        if (ContainsId(root, "timeoutPlayerIds", idStr))
        {
            return PlayerReplacementReason.ClockTimeout;
        }

        return null;
    }

    private static bool ContainsId(JsonElement root, string propertyName, string idStr)
    {
        if (
            !root.TryGetProperty(propertyName, out var arrEl)
            || arrEl.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        foreach (var el in arrEl.EnumerateArray())
        {
            if (
                el.ValueKind == JsonValueKind.String
                && string.Equals(el.GetString(), idStr, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }
}
