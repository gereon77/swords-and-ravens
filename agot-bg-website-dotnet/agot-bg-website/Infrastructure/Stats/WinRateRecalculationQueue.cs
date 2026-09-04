using System.Threading.Channels;

namespace agot_bg_website.Infrastructure.Stats;

/// <summary>
/// Queues user ids whose win-rate stats need recalculating, consumed by
/// <see cref="WinRateRecalculationBackgroundService"/>. Backed by an unbounded
/// <see cref="Channel{T}"/> rather than raw fire-and-forget <c>Task.Run</c> calls so a burst of
/// games finishing at once (e.g. several PBEM games completing around the same daily tick) is
/// processed one user at a time against a single short-lived DB scope each, instead of spawning
/// unbounded concurrent EF Core contexts. Registered as a singleton (for <see cref="Enqueue"/>)
/// - see Program.cs.
/// </summary>
public sealed class WinRateRecalculationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    /// <summary>Queues a user for background stats recalculation. Safe to call for the same user
    /// multiple times in a row (e.g. every participant of a just-finished game); duplicate
    /// enqueues just mean that user's stats get recomputed more than once in a row, which is
    /// harmless and cheap.</summary>
    public void Enqueue(Guid userId) => _channel.Writer.TryWrite(userId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
