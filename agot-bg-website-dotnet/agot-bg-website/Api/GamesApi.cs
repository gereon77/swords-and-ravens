using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// GET/PATCH /api/game/{id} and GET /api/game/{id}/isCancelled. The PATCH handler implements the
/// "delete all + recreate" idempotent replace pattern for both Players and PreviousPlayers, same
/// as Django's GameSerializer.update — see MIGRATION_PLAN.md §6/§6.1.
/// </summary>
public static class GamesApi
{
    // Wire format uses the game server's own string constants (VOTE / CLOCK_TIMEOUT /
    // REPLACED_BY_PLAYER — see MIGRATION_PLAN.md §4.4/§6.1), which don't match the C# enum
    // member names, so this can't be a plain Enum.Parse.
    private static readonly Dictionary<string, PlayerReplacementReason> ReasonWireMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VOTE"] = PlayerReplacementReason.Vote,
        ["CLOCK_TIMEOUT"] = PlayerReplacementReason.ClockTimeout,
        ["REPLACED_BY_PLAYER"] = PlayerReplacementReason.ReplacedByPlayer
    };

    public static RouteGroupBuilder MapGamesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/game").RequireAuthorization(Infrastructure.Auth.MasterApiAuthenticationHandler.SchemeName);

        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
            return game is null ? Results.NotFound() : Results.Ok(ToDto(game));
        });

        group.MapGet("/{id:guid}/isCancelled", async (Guid id, ApplicationDbContext db) =>
        {
            var state = await db.Games.Where(g => g.Id == id).Select(g => (GameState?)g.State).FirstOrDefaultAsync();
            return state is null ? Results.NotFound() : Results.Ok(new { isCancelled = state == GameState.Cancelled });
        });

        group.MapPatch("/{id:guid}", async (Guid id, GamePatchDto patch, ApplicationDbContext db) =>
        {
            var game = await db.Games
                .Include(g => g.Players)
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

            if (patch.State is not null && Enum.TryParse<GameState>(patch.State, ignoreCase: true, out var parsedState))
            {
                game.State = parsedState;
            }

            if (patch.Players is not null)
            {
                db.PlayersInGame.RemoveRange(game.Players);
                game.Players = patch.Players.Select(p => new PlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    UserId = p.User,
                    Data = JsonDocument.Parse(p.Data.GetRawText())
                }).ToList();
            }

            // See MIGRATION_PLAN.md §6.1/§4.4 — the one new field. Full-replace, same idempotent
            // pattern as Players, so repeated saves of an ongoing game are safe to retry.
            if (patch.PreviousPlayers is not null)
            {
                db.PreviousPlayersInGame.RemoveRange(game.PreviousPlayers);
                game.PreviousPlayers = patch.PreviousPlayers.Select(p => new PreviousPlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    UserId = p.User,
                    House = p.House,
                    Reason = ReasonWireMap.TryGetValue(p.Reason, out var reason)
                        ? reason
                        : throw new ArgumentException($"Unknown previous-player reason '{p.Reason}'"),
                    WasWinner = p.WasWinner,
                    SequenceNumber = p.SequenceNumber,
                    ReplacedAt = p.ReplacedAt
                }).ToList();
            }

            game.UpdatedAt = DateTimeOffset.UtcNow;
            if (patch.UpdateLastActive == true)
            {
                game.LastActiveAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync();
            return Results.Ok(ToDto(game));
        });

        return group;
    }

    private static GameDto ToDto(Game game) => new(
        game.Id,
        game.Name,
        game.OwnerUserId,
        game.SerializedGame?.RootElement,
        game.Version,
        game.State.ToString(),
        game.ViewOfGame?.RootElement);
}
