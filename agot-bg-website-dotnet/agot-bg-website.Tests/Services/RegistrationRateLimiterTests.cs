using agot_bg_website.Services;
using Xunit;

namespace agot_bg_website.Tests.Services;

public class RegistrationRateLimiterTests
{
    [Fact]
    public void TryAcquire_WithinLimit_Succeeds()
    {
        using var limiter = new RegistrationRateLimiter();

        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.TryAcquire("203.0.113.1"));
        }
    }

    [Fact]
    public void TryAcquire_BeyondLimit_IsRejected()
    {
        using var limiter = new RegistrationRateLimiter();

        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquire("203.0.113.2");
        }

        Assert.False(limiter.TryAcquire("203.0.113.2"));
    }

    [Fact]
    public void TryAcquire_DifferentIpAddresses_AreTrackedIndependently()
    {
        using var limiter = new RegistrationRateLimiter();

        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquire("203.0.113.3");
        }

        // A different IP must not be affected by another address's exhausted bucket.
        Assert.True(limiter.TryAcquire("203.0.113.4"));
    }

    [Fact]
    public void TryAcquire_NullOrBlankIpAddress_StillEnforcesALimit()
    {
        using var limiter = new RegistrationRateLimiter();

        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.TryAcquire(null));
        }

        Assert.False(limiter.TryAcquire(null));
        Assert.False(limiter.TryAcquire(""));
    }
}
