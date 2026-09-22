using agot_bg_website.Services;

namespace agot_bg_website.Infrastructure.Stats;

/// <summary>
/// Drains <see cref="UserStatsRecalculationQueue"/> and recomputes each user's cached stats (won
/// games, finished games, removed-from-game count and win rate - see <see
/// cref="UserStatsService.RecalculateAsync"/>, which is the single place all of these are computed
/// together; there is no separate "win rate only" recalculation path), one user at a time, each in
/// its own DI scope (so each gets a short-lived <c>ApplicationDbContext</c> rather than sharing one
/// across the whole app lifetime). Registered as both a singleton (so <see
/// cref="UserStatsRecalculationQueue"/> can be injected wherever a game finishes, e.g.
/// Api.GamesApi's PATCH handler, or an admin triggers a bulk recalculation) and an <see
/// cref="IHostedService"/> - see Program.cs and ChatBroadcaster for the same pattern.
///
/// Processing is already strictly sequential (a single reader draining one unbounded channel -
/// see the queue's doc comment), so a burst/bulk enqueue can never spawn concurrent DB load. On
/// top of that, this loop pauses briefly every <see cref="BatchSize"/> users so a large bulk
/// recalculation (e.g. "recalculate all users" from the admin panel, enqueuing every user at once)
/// yields CPU/DB connections back to the rest of the app in between batches instead of hammering
/// the database back-to-back for however long the whole run takes.
/// </summary>
public sealed class UserStatsRecalculationBackgroundService(
    UserStatsRecalculationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<UserStatsRecalculationBackgroundService> logger
) : BackgroundService
{
    private const int BatchSize = 25;
    private static readonly TimeSpan PauseBetweenBatches = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processedSinceLastPause = 0;

        await foreach (var userId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var statsService = scope.ServiceProvider.GetRequiredService<UserStatsService>();
                await statsService.RecalculateAsync(userId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One user's stats failing to recalculate (e.g. a transient DB hiccup) must never
                // take the whole background loop down - the next game that finishes for this user
                // will simply re-enqueue and retry.
                logger.LogError(ex, "Failed to recalculate cached stats for user {UserId}", userId);
            }

            processedSinceLastPause++;
            if (processedSinceLastPause >= BatchSize)
            {
                processedSinceLastPause = 0;
                await Task.Delay(PauseBetweenBatches, stoppingToken);
            }
        }
    }
}
