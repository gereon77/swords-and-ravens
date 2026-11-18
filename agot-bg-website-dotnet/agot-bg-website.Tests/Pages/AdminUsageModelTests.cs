using agot_bg_website.Areas.Admin.Pages.Usage;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace agot_bg_website.Tests.Pages;

public class AdminUsageModelTests
{
    [Fact]
    public async Task ListsOnlyCurrentHourActiveAccountsOrderedByRequests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        db.Users.AddRange(
            new ApplicationUser
            {
                UserName = "low",
                UsageHour = hour,
                UsageRequests = 1,
            },
            new ApplicationUser
            {
                UserName = "high",
                UsageHour = hour,
                UsageRequests = 10,
            },
            new ApplicationUser
            {
                UserName = "old",
                UsageHour = hour.AddHours(-1),
                UsageRequests = 100,
            },
            new ApplicationUser
            {
                UserName = "deleted",
                IsDeleted = true,
                UsageHour = hour,
                UsageRequests = 200,
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var model = new IndexModel(db, new ConfigurationBuilder().Build());
        await model.OnGetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["high", "low"], model.ActiveUsers.Select(u => u.UserName));
    }
}
