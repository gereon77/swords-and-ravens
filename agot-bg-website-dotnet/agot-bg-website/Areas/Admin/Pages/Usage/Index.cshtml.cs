using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.Usage;

public class IndexModel(ApplicationDbContext db, IConfiguration configuration) : PageModel
{
    public sealed record UserUsage(Guid Id, string? UserName, long Requests);

    public bool Enabled => configuration.GetValue("UsageMetrics:Enabled", true);
    public IReadOnlyList<UsageMetric> Metrics { get; private set; } = [];
    public IReadOnlyList<UserUsage> ActiveUsers { get; private set; } = [];
    public DateTimeOffset CurrentHour { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        Metrics = await db
            .UsageMetrics.AsNoTracking()
            .Where(m => m.Hour >= cutoff)
            .OrderByDescending(m => m.Hour)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        CurrentHour = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            0,
            0,
            TimeSpan.Zero
        );
        ActiveUsers = await db
            .Users.AsNoTracking()
            .Where(u => !u.IsDeleted && u.UsageHour == CurrentHour && u.UsageRequests > 0)
            .OrderByDescending(u => u.UsageRequests)
            .ThenBy(u => u.Id)
            .Take(50)
            .Select(u => new UserUsage(u.Id, u.UserName, u.UsageRequests))
            .ToListAsync(cancellationToken);
    }
}
