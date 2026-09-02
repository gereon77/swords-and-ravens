using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// GET/PATCH /api/game/{id} and GET /api/game/{id}/isCancelled. The PATCH handler implements the
/// "delete all + recreate" idempotent replace pattern for both Players and PreviousPlayers, same
/// as Django's GameSerializer.update — see MIGRATION_PLAN.md §6/§6.1. New Players/PreviousPlayers
/// rows are explicitly added via <c>AddRange</c> rather than just assigned to the navigation
/// collection: since their <c>Id</c> is a client-set (non-default) Guid, EF Core's automatic graph
/// fixup otherwise assumes the row already exists and generates an UPDATE instead of an INSERT,
/// which affects 0 rows and throws <see cref="DbUpdateConcurrencyException"/>
/// on every save that adds a player — see GamesApiPlayerReplacementTests for a pinned repro. PATCH
/// also acquires a per-game <see cref="GameSaveLock"/> first, as defense-in-depth against the
/// game server's saves for the same game genuinely overlapping — see that type's doc comment.
/// </summary>
public static class GamesApi
{
    // Wire format uses the game server's own string constants (VOTE / CLOCK_TIMEOUT /
    // REPLACED_BY_PLAYER — see MIGRATION_PLAN.md §4.4/§6.1), which don't match the C# enum
    // member names, so this can't be a plain Enum.Parse.
    private static readonly Dictionary<string, PlayerReplacementReason> ReasonWireMap = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["VOTE"] = PlayerReplacementReason.Vote,
        ["CLOCK_TIMEOUT"] = PlayerReplacementReason.ClockTimeout,
        ["REPLACED_BY_PLAYER"] = PlayerReplacementReason.ReplacedByPlayer,
    };

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
            async (Guid id, GamePatchDto patch, ApplicationDbContext db) =>
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

                if (patch.Players is not null)
                {
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
                }

                // See MIGRATION_PLAN.md §6.1/§4.4 — the one new field. Full-replace, same idempotent
                // pattern as Players, so repeated saves of an ongoing game are safe to retry.
                if (patch.PreviousPlayers is not null)
                {
                    db.PreviousPlayersInGame.RemoveRange(game.PreviousPlayers);
                    var newPreviousPlayers = patch
                        .PreviousPlayers.Select(p => new PreviousPlayerInGame
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            UserId = p.User,
                            House = p.House,
                            Reason = ReasonWireMap.TryGetValue(p.Reason, out var reason)
                                ? reason
                                : throw new ArgumentException(
                                    $"Unknown previous-player reason '{p.Reason}'"
                                ),
                            WasWinner = p.WasWinner,
                            SequenceNumber = p.SequenceNumber,
                            ReplacedAt = p.ReplacedAt,
                        })
                        .ToList();
                    // Same explicit-Add reasoning as Players above.
                    db.PreviousPlayersInGame.AddRange(newPreviousPlayers);
                    game.PreviousPlayers = newPreviousPlayers;
                }

                game.UpdatedAt = DateTimeOffset.UtcNow;
                if (patch.UpdateLastActive == true)
                {
                    game.LastActiveAt = DateTimeOffset.UtcNow;
                }

                await db.SaveChangesAsync();
                return Results.Ok(ToDto(game));
            }
        );

        return group;
    }

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
