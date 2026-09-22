using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Paging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.PreviousPlayerCleanup;

/// <summary>
/// One-off cleanup tool for <see cref="PreviousPlayerInGame"/> rows created by the now-fixed
/// lobby-seat-change bug (see <see cref="agot_bg_website.Domain.PreviousPlayerCleanup"/>'s doc
/// comment for exactly how these are identified and why it's safe). Deliberately a manual,
/// review-before-delete admin page rather than an automatic EF Core data migration or a separate
/// console tool: this reuses the already-deployed auth/DB context/stats-recalculation
/// infrastructure instead of needing a new deployable artifact or blocking app startup, and lets
/// an admin see exactly which rows are about to be deleted first, since deleting is irreversible.
/// </summary>
public class IndexModel(
    ApplicationDbContext db,
    Infrastructure.Stats.UserStatsRecalculationQueue userStatsQueue
) : PageModel
{
    private const int DefaultPageSize = 20;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    public List<PreviousPlayerInGame> Rows { get; set; } = [];

    public PagerInfo Pager { get; set; } = null!;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        if (!Request.Query.ContainsKey("pageSize"))
        {
            PageSize = PageSizeCookie.Read(Request, DefaultPageSize);
        }
        PageSize = PagingExtensions.NormalizePageSize(PageSize, DefaultPageSize);

        var query = db
            .PreviousPlayersInGame.Include(p => p.User)
            .Include(p => p.Game)
            .Where(Domain.PreviousPlayerCleanup.IsLikelyLobbyChurnArtifact);

        var paged = await query
            .OrderByDescending(p => p.ReplacedAt)
            .ToPagedResultAsync(PageNumber, PageSize);
        Rows = paged.Items;
        Pager = paged.Pager;
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        // Re-check the predicate server-side rather than trusting the posted id alone, so this
        // can never be used to delete a legitimate (resolved-reason, or legacy-backfilled) row.
        var row = await db
            .PreviousPlayersInGame.Where(Domain.PreviousPlayerCleanup.IsLikelyLobbyChurnArtifact)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (row is null)
        {
            return NotFound();
        }

        db.PreviousPlayersInGame.Remove(row);
        await db.SaveChangesAsync();

        // The user's "previously participated games" list just shrank - refresh their cached
        // win-rate stats too, even though this never actually affected the win-rate number itself
        // (only Ongoing/Finished games count towards that).
        userStatsQueue.Enqueue(row.UserId);

        StatusMessage = "Removed 1 corrupted PreviousPlayerInGame row.";
        return RedirectToPage(new { PageNumber, PageSize });
    }

    public async Task<IActionResult> OnPostDeleteAllShownAsync()
    {
        // Deliberately deletes every currently-matching row, not just the current page - the
        // predicate is precise enough (see PreviousPlayerCleanup's doc comment) that there's no
        // value in forcing an admin to page through and delete in batches.
        var rows = await db
            .PreviousPlayersInGame.Where(Domain.PreviousPlayerCleanup.IsLikelyLobbyChurnArtifact)
            .ToListAsync();

        db.PreviousPlayersInGame.RemoveRange(rows);
        await db.SaveChangesAsync();

        userStatsQueue.EnqueueAll(rows.Select(r => r.UserId).Distinct());

        StatusMessage = $"Removed {rows.Count} corrupted PreviousPlayerInGame row(s).";
        return RedirectToPage(new { PageNumber = 1, PageSize });
    }
}
