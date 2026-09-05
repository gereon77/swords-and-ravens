using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Infrastructure.Paging;
using agot_bg_website.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.Users;

public class IndexModel(
    UserManager<ApplicationUser> userManager,
    AccountDeletionService accountDeletionService,
    UserStatsService userStatsService,
    Infrastructure.Stats.UserStatsRecalculationQueue userStatsQueue
) : PageModel
{
    private const int DefaultPageSize = 10;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    public List<ApplicationUser> Users { get; set; } = [];

    public PagerInfo Pager { get; set; } = null!;

    public Dictionary<Guid, IList<string>> RolesByUserId { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        PageSize = PagingExtensions.NormalizePageSize(PageSize, DefaultPageSize);

        var query = userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalized = Search.Trim();
            query = query.Where(u =>
                EF.Functions.ILike(u.UserName!, $"%{normalized}%")
                || EF.Functions.ILike(u.Email!, $"%{normalized}%")
                || u.Id.ToString() == normalized
            );
        }

        var paged = await query.OrderBy(u => u.UserName).ToPagedResultAsync(PageNumber, PageSize);
        Users = paged.Items;
        Pager = paged.Pager;

        foreach (var user in Users)
        {
            RolesByUserId[user.Id] = await userManager.GetRolesAsync(user);
        }
    }

    public async Task<IActionResult> OnPostToggleBanAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (await userManager.IsInRoleAsync(user, RoleNames.Banned))
        {
            await userManager.RemoveFromRoleAsync(user, RoleNames.Banned);
            StatusMessage = $"{user.UserName} has been unbanned.";
        }
        else
        {
            await userManager.AddToRoleAsync(user, RoleNames.Banned);
            // Force the user out of any active session immediately, mirroring the PlayApi banned check.
            await userManager.UpdateSecurityStampAsync(user);
            StatusMessage = $"{user.UserName} has been banned.";
        }

        return RedirectToPage(
            new
            {
                Search,
                PageNumber,
                PageSize,
            }
        );
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var displayName = user.DisplayName;
        await accountDeletionService.DeleteAccountAsync(user);
        StatusMessage = $"{displayName} has Took the Black - their account has been deleted.";

        return RedirectToPage(
            new
            {
                Search,
                PageNumber,
                PageSize,
            }
        );
    }

    /// <summary>
    /// Forces an immediate, synchronous recalculation of a single user's cached win-rate stats
    /// (<see cref="ApplicationUser.CachedWinRate"/> and friends) - the normal path only happens in
    /// the background when one of their games finishes (see Api.GamesApi's PATCH handler) or the
    /// first time their profile is viewed after never having been cached. Useful right after a
    /// change to the win-rate calculation logic itself, to refresh a specific user's numbers
    /// without waiting for their next game.
    /// </summary>
    public async Task<IActionResult> OnPostRecalculateStatsAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        await userStatsService.RecalculateAsync(id);
        StatusMessage = $"Recalculated cached win-rate stats for {user.DisplayName}.";

        return RedirectToPage(
            new
            {
                Search,
                PageNumber,
                PageSize,
            }
        );
    }

    /// <summary>
    /// Bulk variant of <see cref="OnPostRecalculateStatsAsync"/> - enqueues every non-deleted
    /// user for background stats recalculation (see
    /// Infrastructure.Stats.UserStatsRecalculationQueue/UserStatsRecalculationBackgroundService).
    /// Useful after a change to the stats calculation logic itself, to refresh everyone's numbers
    /// without waiting for each user's next game to finish. Only queues ids (no stats are
    /// computed inline here) and the background service already throttles itself between
    /// batches, so this responds immediately regardless of how many users exist.
    /// </summary>
    public async Task<IActionResult> OnPostRecalculateAllStatsAsync()
    {
        var userIds = await userManager
            .Users.Where(u => !u.IsDeleted)
            .Select(u => u.Id)
            .ToListAsync();
        userStatsQueue.EnqueueAll(userIds);
        StatusMessage =
            $"Queued cached stats recalculation for {userIds.Count} user(s). This runs in the background and may take a while.";

        return RedirectToPage(
            new
            {
                Search,
                PageNumber,
                PageSize,
            }
        );
    }
}
