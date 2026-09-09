using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// GET/PATCH /api/game/{id} and GET /api/game/{id}/isCancelled. The PATCH handler implements the
/// "delete all + recreate" idempotent replace pattern for Players, same as Django's
/// GameSerializer.update — see MIGRATION_PLAN.md §6. New Players rows are explicitly added via
/// <c>AddRange</c> rather than just assigned to the navigation collection: since their <c>Id</c>
/// is a client-set (non-default) Guid, EF Core's automatic graph fixup otherwise assumes the row
/// already exists and generates an UPDATE instead of an INSERT, which affects 0 rows and throws
/// <see cref="DbUpdateConcurrencyException"/> on every save that adds a player — see
/// GamesApiPlayerReplacementTests for a pinned repro.
///
/// <c>PreviousPlayerInGame</c> is never sent by the game server (it only ever sends the current
/// <c>Players</c> list) — it's entirely computed here by diffing the old and new player lists on
/// every save: a user present before but missing now is recorded as removed, with <c>Reason</c>
/// resolved from the just-saved <c>ViewOfGame</c>'s <c>oldPlayerIds</c>/<c>timeoutPlayerIds</c> via
/// <see cref="Domain.PreviousPlayerReasonResolver"/> (null only if neither array names the user -
/// see that type's doc comment); a user with an existing row who reappears (voted back in) has
/// that row removed again — see <see cref="DiffPreviousPlayers"/> and
/// GamesApiPreviousPlayerDiffTests.
///
/// PATCH also acquires a per-game <see cref="GameSaveLock"/> first, as defense-in-depth against the
/// game server's saves for the same game genuinely overlapping — see that type's doc comment.
/// </summary>
public static class GamesApi
{
    public static RouteGroupBuilder MapGamesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/game")
            .RequireAuthorization(Infrastructure.Auth.MasterApiAuthenticationHandler.SchemeName);

        group.MapGet(
            "/{id:guid}",
            async (Guid id, ApplicationDbContext db) =>
            {
                var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
                return game is null ? Results.NotFound() : Results.Ok(ToDto(game));
            }
        );

        group.MapGet(
            "/{id:guid}/isCancelled",
            async (Guid id, ApplicationDbContext db) =>
            {
                var state = await db
                    .Games.Where(g => g.Id == id)
                    .Select(g => (GameState?)g.State)
                    .FirstOrDefaultAsync();
                // LiveWebsiteClient.ts's isGameCancelled() reads `response.cancelled` specifically
                // (not `response.is_cancelled`/`isCancelled`), matching Django's exact response shape
                // for this one endpoint — keep the property named "cancelled" even though the
                // snake_case naming policy would otherwise turn "IsCancelled" into "is_cancelled".
                return state is null
                    ? Results.NotFound()
                    : Results.Ok(new { cancelled = state == GameState.Cancelled });
            }
        );

        group.MapPatch(
            "/{id:guid}",
            async (
                Guid id,
                GamePatchDto patch,
                ApplicationDbContext db,
                Infrastructure.Stats.UserStatsRecalculationQueue userStatsQueue
            ) =>
            {
                // See GameSaveLock's doc comment: the game server can (and does, in practice) fire
                // multiple overlapping saves for the same game in quick succession, which otherwise
                // races on the delete-then-recreate Players/PreviousPlayers replace below.
                using var _ = await GameSaveLock.AcquireAsync(id);

                var game = await db
                    .Games.Include(g => g.Players)
                    .Include(g => g.PreviousPlayers)
                    .FirstOrDefaultAsync(g => g.Id == id);

                if (game is null)
                {
                    return Results.NotFound();
                }

                var stateBeforePatch = game.State;

                if (patch.SerializedGame is { } serializedGame)
                {
                    game.SerializedGame = JsonDocument.Parse(serializedGame.GetRawText());
                }

                if (patch.ViewOfGame is { } viewOfGame)
                {
                    game.ViewOfGame = JsonDocument.Parse(viewOfGame.GetRawText());
                }

                if (patch.Version is not null)
                {
                    game.Version = patch.Version;
                }

                if (
                    patch.State is not null
                    && Enum.TryParse<GameState>(patch.State, ignoreCase: true, out var parsedState)
                )
                {
                    game.State = parsedState;
                }

                // A game cancelled before it ever left the lobby (view_of_game.turn still -1, i.e.
                // it never even finished drafting/setup) has nothing worth keeping: no players ever
                // really played, and its chat history is worthless. Delete it outright - game row,
                // public chat room, and all its messages - instead of leaving a dead row around
                // forever. Snr.Migration's ImportGamesAsync applies the exact same rule (and skips
                // importing such games from the legacy DB in the first place) - see its doc comment.
                if (game.State == GameState.Cancelled && IsTurnMinusOne(game.ViewOfGame))
                {
                    var publicChatRoomId =
                        TryGetPublicChatRoomId(game.ViewOfGame)
                        ?? TryGetPublicChatRoomId(game.SerializedGame);
                    if (publicChatRoomId is { } roomId)
                    {
                        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
                        if (room is not null)
                        {
                            db.Rooms.Remove(room); // cascades to Messages/UsersInRoom
                        }
                    }

                    db.Games.Remove(game); // cascades to PlayersInGame/PreviousPlayersInGame
                    await db.SaveChangesAsync();
                    return Results.NoContent();
                }

                if (patch.Players is not null)
                {
                    // Diff against the player list as it stood before this save, and the existing
                    // PreviousPlayerInGame rows, before RemoveRange below clears game.Players.
                    var (toAdd, toRemove) = DiffPreviousPlayers(
                        oldPlayerUserIds: game.Players.Select(p => p.UserId),
                        newPlayerUserIds: patch.Players.Select(p => p.User),
                        existingPreviousPlayerUserIds: game.PreviousPlayers.Select(p => p.UserId)
                    );

                    db.PlayersInGame.RemoveRange(game.Players);
                    var newPlayers = patch
                        .Players.Select(p => new PlayerInGame
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            UserId = p.User,
                            Data = JsonDocument.Parse(p.Data.GetRawText()),
                        })
                        .ToList();
                    // Explicitly Add these: assigning a brand-new object to a tracked navigation
                    // collection is NOT enough for EF Core to know it's an INSERT. Because Id is a
                    // client-set (non-default) Guid, EF's automatic graph fixup otherwise assumes the
                    // entity already exists and generates an UPDATE instead of an INSERT — which then
                    // affects 0 rows and throws DbUpdateConcurrencyException on every single save that
                    // adds a player, not just under concurrent requests.
                    db.PlayersInGame.AddRange(newPlayers);
                    game.Players = newPlayers;

                    if (toRemove.Count > 0)
                    {
                        db.PreviousPlayersInGame.RemoveRange(
                            game.PreviousPlayers.Where(p => toRemove.Contains(p.UserId))
                        );
                    }

                    if (toAdd.Count > 0)
                    {
                        // Reason is resolved from the just-saved ViewOfGame's flat top-level
                        // oldPlayerIds/timeoutPlayerIds arrays (same logic Snr.Migration's historical
                        // backfill uses, see PreviousPlayerReasonResolver) - null only if the removed
                        // user appears in neither (e.g. a replace-player-by-player/vassal swap this
                        // data model doesn't otherwise track). Not used for win-rate calculation
                        // either way (every PreviousPlayerInGame row counts as a loss regardless of
                        // Reason — see MIGRATION_PLAN.md §10.2).
                        db.PreviousPlayersInGame.AddRange(
                            toAdd.Select(userId => new PreviousPlayerInGame
                            {
                                Id = Guid.NewGuid(),
                                GameId = game.Id,
                                UserId = userId,
                                Reason = PreviousPlayerReasonResolver.Resolve(
                                    game.ViewOfGame,
                                    userId
                                ),
                                ReplacedAt = DateTimeOffset.UtcNow,
                            })
                        );
                    }
                }

                game.UpdatedAt = DateTimeOffset.UtcNow;
                if (patch.UpdateLastActive == true)
                {
                    game.LastActiveAt = DateTimeOffset.UtcNow;
                }

                await db.SaveChangesAsync();

                // Every current and former participant's cached win-rate stats become stale the
                // moment a game they were part of finishes - recompute them in the background
                // rather than on their next profile page view (see UserStatsService's doc
                // comment). Not raised for any other state transition (a game going back from
                // Finished to something else can't currently happen, and no other transition
                // changes anyone's win-rate facts).
                if (stateBeforePatch != GameState.Finished && game.State == GameState.Finished)
                {
                    foreach (
                        var userId in game
                            .Players.Select(p => p.UserId)
                            .Concat(game.PreviousPlayers.Select(p => p.UserId))
                            .Distinct()
                    )
                    {
                        userStatsQueue.Enqueue(userId);
                    }
                }

                return Results.Ok(ToDto(game));
            }
        );

        return group;
    }

    /// <summary>
    /// Pure diff between the player list before and after a save, plus the set of users who
    /// already have a PreviousPlayerInGame row: returns who should gain a new row (present before,
    /// missing now, no existing row yet) and who should have their existing row removed (missing
    /// before but present again now - voted back in). Extracted as a pure, internal helper so
    /// GamesApiPreviousPlayerDiffTests can exercise every case directly without a database.
    /// </summary>
    internal static (List<Guid> ToAdd, List<Guid> ToRemove) DiffPreviousPlayers(
        IEnumerable<Guid> oldPlayerUserIds,
        IEnumerable<Guid> newPlayerUserIds,
        IEnumerable<Guid> existingPreviousPlayerUserIds
    )
    {
        var oldSet = oldPlayerUserIds.ToHashSet();
        var newSet = newPlayerUserIds.ToHashSet();
        var existingSet = existingPreviousPlayerUserIds.ToHashSet();

        var toAdd = oldSet.Except(newSet).Where(id => !existingSet.Contains(id)).ToList();
        var toRemove = existingSet.Where(newSet.Contains).ToList();
        return (toAdd, toRemove);
    }

    // internal (not private) so GamesApiCancelledLobbyGameTests can exercise them directly.
    internal static bool IsTurnMinusOne(JsonDocument? viewOfGame) =>
        viewOfGame is not null
        && viewOfGame.RootElement.TryGetProperty("turn", out var turnEl)
        && turnEl.ValueKind == JsonValueKind.Number
        && turnEl.GetInt32() == -1;

    internal static Guid? TryGetPublicChatRoomId(JsonDocument? doc) =>
        doc is not null
        && doc.RootElement.TryGetProperty("publicChatRoomId", out var el)
        && el.ValueKind == JsonValueKind.String
        && Guid.TryParse(el.GetString(), out var id)
            ? id
            : null;

    private static GameDto ToDto(Game game) =>
        new(
            game.Id,
            game.Name,
            game.OwnerUserId,
            game.SerializedGame?.RootElement,
            game.Version,
            game.State.ToString(),
            game.ViewOfGame?.RootElement
        );
}
