using agot_bg_website.Data;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Infrastructure.UsageMetrics;

internal sealed class UsageMetricsWriter(
    UsageMetricsCollector collector,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<UsageMetricsWriter> logger
) : BackgroundService
{
    private readonly bool enabled = configuration.GetValue("UsageMetrics:Enabled", true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UsageBatch? pending = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (enabled)
                {
                    pending ??= collector.Snapshot(DateTime.UtcNow);
                    await SaveAsync(pending, stoppingToken);
                    pending = null;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist usage metrics; will retry.");
            }

            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clean up expired usage metrics; will retry.");
            }
        }
    }

    internal async Task SaveAsync(UsageBatch batch, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "UsageMetricBatches" ("Id", "CreatedAt") VALUES ({batch.Id}, {batch.Hour}) ON CONFLICT ("Id") DO NOTHING""",
            cancellationToken
        );
        if (inserted == 1)
        {
            foreach (var value in batch.Values)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO "UsageMetrics" ("Hour", "Category", "Outcome", "Requests", "DurationSeconds", "ProcessSamples", "ProcessCpuPercentSum", "ProcessMemoryBytesMax")
                    VALUES ({batch.Hour}, {value.Category}, {value.Outcome}, {value.Count}, {value.Seconds}, 0, 0, 0)
                    ON CONFLICT ("Hour", "Category", "Outcome") DO UPDATE SET
                        "Requests" = "UsageMetrics"."Requests" + EXCLUDED."Requests",
                        "DurationSeconds" = "UsageMetrics"."DurationSeconds" + EXCLUDED."DurationSeconds"
                    """,
                    cancellationToken
                );
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "UsageMetrics" ("Hour", "Category", "Outcome", "Requests", "DurationSeconds", "ProcessSamples", "ProcessCpuPercentSum", "ProcessMemoryBytesMax")
                VALUES ({batch.Hour}, {"process"}, {"sample"}, 0, 0, 1, {batch.CpuPercent}, {batch.MemoryBytes})
                ON CONFLICT ("Hour", "Category", "Outcome") DO UPDATE SET
                    "ProcessSamples" = "UsageMetrics"."ProcessSamples" + 1,
                    "ProcessCpuPercentSum" = "UsageMetrics"."ProcessCpuPercentSum" + EXCLUDED."ProcessCpuPercentSum",
                    "ProcessMemoryBytesMax" = GREATEST("UsageMetrics"."ProcessMemoryBytesMax", EXCLUDED."ProcessMemoryBytesMax")
                """,
                cancellationToken
            );
        }
        await transaction.CommitAsync(cancellationToken);
    }

    internal async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        await db
            .Users.Where(u => u.UsageHour != null && u.UsageHour < hour)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(u => u.UsageHour, (DateTimeOffset?)null)
                        .SetProperty(u => u.UsageRequests, 0),
                cancellationToken
            );
        var cutoff = now.UtcDateTime.AddDays(-7);
        await db.UsageMetrics.Where(m => m.Hour < cutoff).ExecuteDeleteAsync(cancellationToken);
        await db
            .UsageMetricBatches.Where(b => b.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
