using agot_bg_website.Infrastructure;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

public class RelativeTimeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JustNow_ReturnsAMoment()
    {
        var result = RelativeTimeFormatter.Format(Now.AddSeconds(-5), Now);

        Assert.Equal("a moment ago", result);
    }

    [Fact]
    public void FewMinutesAgo_PluralizesCorrectly()
    {
        Assert.Equal("1 minute ago", RelativeTimeFormatter.Format(Now.AddMinutes(-1), Now));
        Assert.Equal("5 minutes ago", RelativeTimeFormatter.Format(Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void FewHoursAgo_PluralizesCorrectly()
    {
        Assert.Equal("2 hours ago", RelativeTimeFormatter.Format(Now.AddHours(-2), Now));
    }

    [Fact]
    public void FewDaysAgo_PluralizesCorrectly()
    {
        Assert.Equal("3 days ago", RelativeTimeFormatter.Format(Now.AddDays(-3), Now));
    }

    [Fact]
    public void FutureTimestamp_ReturnsFromNow()
    {
        Assert.Equal("2 hours from now", RelativeTimeFormatter.Format(Now.AddHours(2), Now));
    }
}
