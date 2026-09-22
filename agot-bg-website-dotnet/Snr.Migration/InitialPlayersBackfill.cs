using System.Text.Json;
using System.Text.Json.Nodes;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;

namespace Snr.Migration;

/// <summary>
/// One-time backfill of `ViewOfGame.initialPlayerIds` for games that reached the ingame state
/// before the game server started persisting this field itself (see IngameGameState.ts's
/// initialPlayerIds field and serializedGameMigrations.ts version "137" for how it's derived and
/// self-heals for games the game server still loads/saves).
///
/// `initialPlayerIds` refines the "replacer" win-rate exemption (MIGRATION_PLAN.md §14): a user
/// who was one of the game's *original* players - even if they were later replaced and then
/// separately jumped back in as a replacer for a different house - must not get the "no downside
/// risk" exemption for their own removal(s), since they didn't just help finish someone else's
/// stalling game.
///
/// A game the live game server still loads (any still-Ongoing game, or a Finished/Cancelled one a
/// client happens to revisit) self-heals for free the moment it's next saved - the game server's
/// own load-time migration recomputes `initialPlayerIds` from the "user-house-assignments" game
/// log entry and `EntireGame.getViewOfGame()` then includes it in what gets persisted. Only
/// Finished/Cancelled games that nobody ever revisits again need this tool, since nothing will
/// ever trigger a fresh save for them otherwise.
///
/// Unlike <see cref="PreviousPlayersBackfill"/> (which deliberately avoids `SerializedGame`
/// entirely - see GameEntities.cs's doc comment), this tool DOES have to parse it: the
/// "user-house-assignments" log entry it needs only exists inside
/// `childGameState.gameLogManager.logs` and `ViewOfGame` carries no game-log data at all. That
/// makes this noticeably more expensive per game than the website's usual `ViewOfGame`-only reads
/// (`SerializedGame` can be multiple MB), which is why it's a separate, explicitly-invoked one-time
/// tool rather than folded into the regular `import`/`verify` steps or run automatically.
///
/// Safe to re-run: it only ever touches games whose `ViewOfGame` doesn't already have an
/// `initialPlayerIds` property, so an already-backfilled (or since-resaved) game is skipped.
/// </summary>
public static class InitialPlayersBackfill
{
    public static async Task RunAsync(string targetConnectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(targetConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);

        // Cheap first pass: only State/ViewOfGame, no SerializedGame - lets us skip games that
        // never reached ingame (still-InLobby/lobby-cancelled: ViewOfGame.turn == -1, no players,
        // no log to derive anything from) or were already backfilled/resaved, without ever paying
        // for a multi-MB SerializedGame read. The initialPlayerIds check itself can't translate to
        // SQL (it inspects opaque JSON), so it's applied in-memory after this projection comes back.
        var candidates = await db
            .Games.Where(g =>
                (g.State == GameState.Finished || g.State == GameState.Cancelled)
                && g.ViewOfGame != null
            )
            .Select(g => new { g.Id, g.ViewOfGame })
            .ToListAsync();
        var candidateIds = candidates
            .Where(g => !HasInitialPlayerIds(g.ViewOfGame))
            .Select(g => g.Id)
            .ToList();

        Console.WriteLine(
            $"---> Backfilling initialPlayerIds for up to {candidateIds.Count} candidate game(s)"
        );

        var backfilled = 0;
        var skippedNoLog = 0;
        var processed = 0;
        foreach (var id in candidateIds)
        {
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id);
            processed++;
            if (game?.SerializedGame is null || game.ViewOfGame is null)
            {
                continue;
            }

            if (TryComputeInitialPlayerIds(game.SerializedGame, out var initialPlayerIds))
            {
                game.ViewOfGame = WithInitialPlayerIds(game.ViewOfGame, initialPlayerIds);
                backfilled++;
            }
            else
            {
                skippedNoLog++;
            }

            // SerializedGame blobs are large, so this batches much smaller than
            // Importer.DefaultSaveBatchSize to keep peak memory reasonable.
            if (processed % 50 == 0)
            {
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                Console.WriteLine($"    ...{processed} / {candidateIds.Count} processed so far");
            }
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Console.WriteLine(
            $"    initialPlayerIds backfill: {backfilled} game(s) backfilled, "
                + $"{skippedNoLog} skipped (never reached ingame / no assignments log found)"
        );
    }

    private static bool HasInitialPlayerIds(JsonDocument? viewOfGame) =>
        viewOfGame is not null && viewOfGame.RootElement.TryGetProperty("initialPlayerIds", out _);

    /// <summary>
    /// Mirrors serializedGameMigrations.ts version "137": finds the game's single
    /// "user-house-assignments" log entry (logged once, in IngameGameState.beginGame()) and
    /// returns the user ids from its `assignments` tuples. Returns false (rather than an empty
    /// list) when the game never reached ingame at all, or - which should not normally happen for
    /// any game that did - the log entry can't be found, so callers can tell "no original players
    /// because this game never really started" apart from "couldn't derive it".
    /// </summary>
    internal static bool TryComputeInitialPlayerIds(
        JsonDocument serializedGame,
        out List<Guid> initialPlayerIds
    )
    {
        initialPlayerIds = [];

        var root = serializedGame.RootElement;
        if (
            !root.TryGetProperty("childGameState", out var childGameStateEl)
            || childGameStateEl.ValueKind != JsonValueKind.Object
            || !childGameStateEl.TryGetProperty("type", out var typeEl)
            || typeEl.GetString() != "ingame"
        )
        {
            // Cancelled directly from the lobby (LobbyGameState.ts's own cancel path) never calls
            // beginGame() - EntireGame.childGameState stays "lobby"/"cancelled" and never becomes
            // "ingame" at all. A game cancelled *during* play instead nests CancelledGameState as
            // ingame's own child (see IngameGameState.ts/VoteType.ts), so this top-level check
            // alone is enough to also cover those - no separate "cancelled" case needed here.
            return false;
        }

        if (
            !childGameStateEl.TryGetProperty("gameLogManager", out var logManagerEl)
            || !logManagerEl.TryGetProperty("logs", out var logsEl)
            || logsEl.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        foreach (var logEl in logsEl.EnumerateArray())
        {
            if (
                !logEl.TryGetProperty("data", out var dataEl)
                || !dataEl.TryGetProperty("type", out var logTypeEl)
                || logTypeEl.GetString() != "user-house-assignments"
            )
            {
                continue;
            }

            if (
                dataEl.TryGetProperty("assignments", out var assignmentsEl)
                && assignmentsEl.ValueKind == JsonValueKind.Array
            )
            {
                foreach (var pairEl in assignmentsEl.EnumerateArray())
                {
                    if (pairEl.ValueKind != JsonValueKind.Array || pairEl.GetArrayLength() != 2)
                    {
                        continue;
                    }
                    var userIdEl = pairEl[1];
                    if (
                        userIdEl.ValueKind == JsonValueKind.String
                        && Guid.TryParse(userIdEl.GetString(), out var userId)
                    )
                    {
                        initialPlayerIds.Add(userId);
                    }
                }
            }
            return true; // exactly one such log entry is ever recorded, so we're done either way
        }

        return false;
    }

    private static JsonDocument WithInitialPlayerIds(
        JsonDocument viewOfGame,
        List<Guid> initialPlayerIds
    )
    {
        var node =
            JsonNode.Parse(viewOfGame.RootElement.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("ViewOfGame root must be a JSON object");

        node["initialPlayerIds"] = new JsonArray(
            initialPlayerIds.Select(id => JsonValue.Create(id.ToString())).ToArray<JsonNode?>()
        );

        return JsonDocument.Parse(node.ToJsonString());
    }
}
