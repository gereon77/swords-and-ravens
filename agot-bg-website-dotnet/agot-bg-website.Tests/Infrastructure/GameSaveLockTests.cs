using agot_bg_website.Infrastructure;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

/// <summary>
/// Covers <see cref="GameSaveLock"/>: defense-in-depth serialization of the PATCH handler's
/// read-modify-write cycle per game, since the game server's fire-and-forget <c>saveGame()</c>
/// calls can genuinely overlap for the same game. Note: the specific
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> originally observed in
/// local dev turned out to reproduce even for a single, non-overlapping save — see
/// <c>GamesApiPlayerReplacementTests</c> and <c>Api/GamesApi.cs</c>'s doc comment for that actual
/// root cause and its fix (explicit <c>AddRange</c> for new Players/PreviousPlayers rows). This
/// lock remains a good safety net against real overlapping-save races on the delete side, even
/// though it wasn't the primary cause of the reported exception.
/// </summary>
public class GameSaveLockTests
{
    [Fact]
    public async Task AcquireAsync_SerializesConcurrentAcquisitionsForTheSameGameId()
    {
        var gameId = Guid.NewGuid();
        var concurrentSectionEntries = 0;
        var maxObservedConcurrency = 0;
        var lockObj = new object();

        async Task RunCriticalSectionAsync()
        {
            using var _ = await GameSaveLock.AcquireAsync(gameId);

            lock (lockObj)
            {
                concurrentSectionEntries++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentSectionEntries);
            }

            // Simulate the read-modify-write work the PATCH handler does, giving a second,
            // overlapping acquisition attempt every opportunity to (incorrectly) run concurrently
            // if the lock didn't actually serialize them.
            await Task.Delay(50);

            lock (lockObj)
            {
                concurrentSectionEntries--;
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => RunCriticalSectionAsync()));

        Assert.Equal(1, maxObservedConcurrency);
    }

    [Fact]
    public async Task AcquireAsync_DoesNotSerializeAcquisitionsForDifferentGameIds()
    {
        var gameIdA = Guid.NewGuid();
        var gameIdB = Guid.NewGuid();

        using var lockA = await GameSaveLock.AcquireAsync(gameIdA);

        // A lock held for game A must not block an unrelated game B's save from proceeding —
        // otherwise one busy game would stall PATCH requests for every other game too.
        var acquireBTask = GameSaveLock.AcquireAsync(gameIdB);
        var completed = await Task.WhenAny(acquireBTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(acquireBTask, completed);
        (await acquireBTask).Dispose();
    }

    [Fact]
    public async Task Dispose_ReleasesTheLockSoASubsequentAcquireCanProceed()
    {
        var gameId = Guid.NewGuid();

        var first = await GameSaveLock.AcquireAsync(gameId);
        first.Dispose();

        var acquireAgainTask = GameSaveLock.AcquireAsync(gameId);
        var completed = await Task.WhenAny(acquireAgainTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(acquireAgainTask, completed);
        (await acquireAgainTask).Dispose();
    }

    [Fact]
    public async Task Dispose_IsSafeToCallMoreThanOnce()
    {
        var gameId = Guid.NewGuid();

        var handle = await GameSaveLock.AcquireAsync(gameId);
        handle.Dispose();
        handle.Dispose();

        // A double-dispose must not over-release the semaphore (which would let two concurrent
        // acquisitions both succeed at once).
        using var second = await GameSaveLock.AcquireAsync(gameId);
        var thirdTask = GameSaveLock.AcquireAsync(gameId);
        var completed = await Task.WhenAny(thirdTask, Task.Delay(TimeSpan.FromMilliseconds(200)));

        Assert.NotSame(thirdTask, completed);
    }
}
