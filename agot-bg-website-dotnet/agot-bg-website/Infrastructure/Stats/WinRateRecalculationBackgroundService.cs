using agot_bg_website.Services;

namespace agot_bg_website.Infrastructure.Stats;

/// <summary>
/// Drains <see cref="WinRateRecalculationQueue"/> and recomputes each user's cached win-rate stats
/// via <see cref="UserStatsService"/>, one user at a time, each in its own DI scope (so each gets a
/// short-lived <c>ApplicationDbContext</c> rather than sharing one across the whole app lifetime).
/// Registered as both a singleton (so <see cref="WinRateRecalculationQueue"/> can be injected
/// wherever a game finishes, e.g. Api.GamesApi's PATCH handler) and an <see
/// cref="IHostedService"/> - see Program.cs and ChatBroadcaster for the same pattern.
/// </summary>
public sealed class WinRateRecalculationBackgroundService(
    WinRateRecalculationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<WinRateRecalculationBackgroundService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                logger.LogError(
                    ex,
                    "Failed to recalculate cached win-rate stats for user {UserId}",
                    userId
                );
            }
        }
    }
}
