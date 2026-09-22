using System.Threading.Channels;

namespace agot_bg_website.Infrastructure.Stats;

/// <summary>
/// Queues user ids whose cached stats (win rate and friends - see
/// <see cref="Services.UserStatsService"/>) need recalculating, consumed by
/// <see cref="UserStatsRecalculationBackgroundService"/>. Backed by an unbounded
/// <see cref="Channel{T}"/> rather than raw fire-and-forget <c>Task.Run</c> calls so a burst of
/// games finishing at once (e.g. several PBEM games completing around the same daily tick), or an
/// admin-triggered "recalculate everyone" bulk enqueue, is processed one user at a time against a
/// single short-lived DB scope each, instead of spawning unbounded concurrent EF Core contexts.
/// Registered as a singleton (for <see cref="Enqueue"/>/<see cref="EnqueueAll"/>) - see
/// Program.cs.
/// </summary>
public sealed class UserStatsRecalculationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    /// <summary>Queues a user for background stats recalculation. Safe to call for the same user
    /// multiple times in a row (e.g. every participant of a just-finished game); duplicate
    /// enqueues just mean that user's stats get recomputed more than once in a row, which is
    /// harmless and cheap.</summary>
    public void Enqueue(Guid userId) => _channel.Writer.TryWrite(userId);

    /// <summary>Queues every given user id for background stats recalculation - used by the
    /// admin "Recalculate stats for all users" button. Just calls <see cref="Enqueue"/> in a loop;
    /// the channel is unbounded and the background service already throttles itself between
    /// batches (see <see cref="UserStatsRecalculationBackgroundService"/>), so this is safe to call
    /// with tens of thousands of ids without blocking the caller or spiking memory/CPU.</summary>
    public void EnqueueAll(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds)
        {
            Enqueue(userId);
        }
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
