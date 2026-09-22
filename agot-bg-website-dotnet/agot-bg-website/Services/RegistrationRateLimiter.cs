using System.Threading.RateLimiting;

namespace agot_bg_website.Services;

/// <summary>
/// Per-IP rate limit for password registration attempts
/// (POST /Identity/Account/Register only - see RegisterModel.OnPostAsync). This is deliberately
/// NOT wired up as app-wide rate-limiting middleware (no <c>AddRateLimiter</c>/<c>UseRateLimiter</c>
/// in Program.cs): it only ever gates the one handler that constructs it, so no other page or API
/// route is affected. It's defense in depth alongside Turnstile/the honeypot field, for a bot that
/// solves Turnstile (or hits the endpoint while Turnstile isn't configured) but still tries to
/// register many accounts from the same address in a short window. Registered as a singleton
/// since the underlying limiter needs to track state across requests for the app's lifetime.
/// </summary>
public sealed class RegistrationRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<
        string,
        string
    >(ipAddress =>
        RateLimitPartition.GetFixedWindowLimiter(
            ipAddress,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
            }
        )
    );

    /// <summary>
    /// Returns true if this attempt is within the limit for the given IP (and counts against it),
    /// false if the limit has already been reached for the current window. A null/blank IP
    /// (should not normally happen for a real HTTP request) is treated as a single shared bucket
    /// rather than bypassing the limit entirely.
    /// </summary>
    public bool TryAcquire(string? ipAddress)
    {
        var key = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress;
        using var lease = _limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
