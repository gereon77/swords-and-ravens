using System.Collections.Concurrent;

namespace agot_bg_website.Infrastructure;

/// <summary>
/// Per-game async lock guarding <c>Api/GamesApi.cs</c>'s PATCH handler.
///
/// The TS game server's <c>GlobalServer.saveGame()</c> is intentionally fire-and-forget (it calls
/// <c>websiteClient.saveGame(...)</c> without awaiting it, and is itself triggered from multiple
/// call sites — e.g. every <c>EntireGame.onSaveGame</c> state change), so it's normal for two or
/// more PATCH requests for the *same* game to be in flight at once (observed in practice: a user
/// taking a seat can trigger several state changes in quick succession). Each PATCH does a
/// "delete all + recreate" full replace of <c>PlayerInGame</c>/<c>PreviousPlayerInGame</c> rows
/// (see MIGRATION_PLAN.md §6). Since each request gets its own scoped <see
/// cref="agot_bg_website.Data.ApplicationDbContext"/>, two overlapping requests can both load the
/// same rows, and whichever's DELETE runs second finds 0 rows left to delete (the first request
/// already removed them) — EF Core reports that as a
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> ("expected to affect 1
/// row(s), but actually affected 0"), even though there's no real conflicting *edit*, just two
/// saves of the same game racing.
///
/// Serializing PATCH requests per game id (this is a single-process app, so an in-memory lock is
/// sufficient — see the Dockerfile/deployment notes in MIGRATION_PLAN.md §8.4) removes the race
/// entirely: whichever request acquires the lock first always sees a fully-committed prior state,
/// and the game server always re-sends the full current state on every save anyway, so no data is
/// lost by making these applies strictly sequential instead of concurrent.
/// </summary>
public static class GameSaveLock
{
    // Never removed: the number of distinct games saved over an app's lifetime is bounded by real
    // usage (thousands, not millions) and each SemaphoreSlim is small, so this is simpler and
    // safer than trying to prune entries out from under a request that might still be queued on
    // one.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static async Task<IDisposable> AcquireAsync(Guid gameId)
    {
        var gate = Locks.GetOrAdd(gameId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
