using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace agot_bg_website.Services;

/// <summary>
/// Caps how many contact-form messages one visitor may send per day (see
/// <see cref="ContactOptions.MaxMessagesPerDay"/>). Separate from
/// <see cref="RegistrationRateLimiter"/>'s in-process limiter on purpose: a daily quota has to
/// survive container restarts/deploys and be shared by every instance, otherwise a restart (or a
/// second replica) silently hands out a fresh allowance.
/// </summary>
public interface IContactRateLimiter
{
    /// <summary>
    /// Counts one message against <paramref name="key"/>'s daily quota and returns true if it was
    /// still within the limit. Counts first and checks afterwards so two concurrent submissions
    /// can't both slip past the quota.
    /// </summary>
    Task<bool> TryAcquireAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Redis-backed implementation: one counter key per visitor per UTC day, expiring at the next UTC
/// midnight so the quota resets on its own without any cleanup job.
/// </summary>
public sealed class ContactRateLimiter(
    IConnectionMultiplexer redis,
    IOptions<ContactOptions> options
) : IContactRateLimiter
{
    private readonly ContactOptions _options = options.Value;

    internal static string BuildRedisKey(string key, DateTimeOffset utcNow) =>
        $"contact:sent:{utcNow.UtcDateTime:yyyy-MM-dd}:{key}";

    public async Task<bool> TryAcquireAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        if (_options.MaxMessagesPerDay <= 0)
        {
            return false;
        }

        var utcNow = DateTimeOffset.UtcNow;
        var db = redis.GetDatabase();
        var redisKey = BuildRedisKey(key, utcNow);

        var count = await db.StringIncrementAsync(redisKey);
        if (count == 1)
        {
            // Only the first increment of the day sets the expiry; re-setting it on every send
            // would keep pushing the reset further out.
            await db.KeyExpireAsync(
                redisKey,
                utcNow.UtcDateTime.Date.AddDays(1) - utcNow.UtcDateTime
            );
        }

        return count <= _options.MaxMessagesPerDay;
    }
}
