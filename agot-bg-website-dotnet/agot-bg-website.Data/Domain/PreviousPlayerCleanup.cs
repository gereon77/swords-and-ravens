using System.Linq.Expressions;

namespace agot_bg_website.Domain;

/// <summary>
/// Identifies <see cref="PreviousPlayerInGame"/> rows created by a now-fixed bug where a lobby
/// seat change (a user standing up/leaving before the game ever started) was mistakenly recorded
/// as a mid-game removal - see GamesApi.cs's <c>DiffPreviousPlayers</c>/<c>gameWasInLobby</c> and
/// MIGRATION_PLAN.md §10.2. Never counted towards win-rate (that's already limited to
/// Ongoing/Finished games), but inflated the "left early" count and the "previously participated
/// games" list for affected users.
///
/// <see cref="Reason"/>-based identification works because, on the current game server, every
/// real mid-game removal path pushes the removed user's id to <c>ingame.oldPlayerIds</c> or
/// <c>ingame.timeoutPlayerIds</c> before deleting them from <c>ingame.players</c> - both the vote
/// removal (<c>IngameGameState.replacePlayerByVassal</c>, reason Vote/ClockTimeout) and the
/// player-for-player replace vote (<c>VoteType.ReplacePlayer.executeAccepted</c>, which also pushes
/// to <c>oldPlayerIds</c>) do this unconditionally. <see cref="PreviousPlayerReasonResolver"/> can
/// therefore only return null for a row created by the live save-game endpoint
/// (<c>ReplacedAt</c> set) if the removal happened before the game ever reached the ingame state -
/// i.e. a lobby seat change, exactly the bug this class targets.
///
/// The one other source of null-<see cref="PreviousPlayerInGame.Reason"/> rows is
/// Snr.Migration's historical backfill (PreviousPlayersBackfill.cs) for legacy games that predate
/// the oldPlayerIds/timeoutPlayerIds feature - those are legitimate untracked removals, not this
/// bug, and are never mistaken for it because the backfill always leaves
/// <see cref="PreviousPlayerInGame.ReplacedAt"/> null (it isn't derivable from the legacy export),
/// while the live endpoint always sets it to the save's timestamp.
/// </summary>
public static class PreviousPlayerCleanup
{
    /// <summary>
    /// True for rows that can only have been created by the lobby-seat-change bug: created by the
    /// live save-game endpoint (<see cref="PreviousPlayerInGame.ReplacedAt"/> is set) yet with no
    /// resolvable <see cref="PreviousPlayerInGame.Reason"/>. Usable directly in an EF Core
    /// <c>Where</c> clause (translates to SQL) or compiled for in-memory checks/tests.
    /// </summary>
    public static readonly Expression<Func<PreviousPlayerInGame, bool>> IsLikelyLobbyChurnArtifact =
        row => row.Reason == null && row.ReplacedAt != null;
}
