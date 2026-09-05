using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Infrastructure.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Pages;

/// <summary>
/// Directory of registered users, gated to logged-in members only (see the "/Users"
/// AuthorizePage convention in Program.cs) - the .NET equivalent of the individual
/// <c>/User/{id}</c> profile page but as a searchable list (Django never had this; it only
/// offered the individual profile page and a "currently online" chat widget). Unlike the Admin
/// area's own Users page (<c>Areas/Admin/Pages/Users/Index.cshtml.cs</c>), this page never
/// exposes email addresses, role/permission editing, or account deletion - it only lets users with
/// the <see cref="GamePermissions.ManageUserStatus"/> permission (Admin and High Member by
/// default) toggle the On probation/Tongueless/Banned status of other, non-staff members.
/// </summary>
public class UsersModel(
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService,
    ApplicationDbContext dbContext,
    Infrastructure.Stats.UserStatsRecalculationQueue userStatsQueue
) : PageModel
{
    private const int DefaultPageSize = 10;

    /// <summary>Roles a moderator is never allowed to alter here, to prevent High Members from
    /// banning/tonguing each other or Admins - only plain Members can be moderated this way.</summary>
    private static readonly string[] ProtectedRoles = [RoleNames.Admin, RoleNames.HighMember];

    /// <summary>Allowed values for <see cref="StatusFilter"/>, mapped to the actual role name to
    /// filter by. Only exposed in the UI to users with <see
    /// cref="GamePermissions.ManageUserStatus"/> (see <see cref="CanManageUserStatus"/>) since
    /// this exists specifically so High Members/Admins can find and undo those statuses.</summary>
    private static readonly Dictionary<string, string> StatusFilterRoles = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["probation"] = RoleNames.OnProbation,
        ["tongueless"] = RoleNames.Tongueless,
        ["banned"] = RoleNames.Banned,
    };

    /// <summary>Allowed values for <see cref="SortBy"/>, and each column's "first click" sort
    /// direction - ranking-style stat columns (finished/won/removed/winrate) default to
    /// descending (best first), while username/created keep the previous implicit ascending
    /// order as their default, matching what users are already used to.</summary>
    private static readonly Dictionary<string, string> SortColumnDefaultDirection = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["username"] = "asc",
        ["finished"] = "desc",
        ["won"] = "desc",
        ["removed"] = "desc",
        ["winrate"] = "desc",
        ["created"] = "desc",
        ["activity"] = "desc",
    };

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "username";

    [BindProperty(SupportsGet = true)]
    public string SortDir { get; set; } = "asc";

    /// <summary>One of <see cref="StatusFilterRoles"/>'s keys ("probation"/"tongueless"/"banned"),
    /// or null/empty for no filter. Only actually applied when <see
    /// cref="CanManageUserStatus"/> is true, so a crafted query string can't be used to enumerate
    /// moderation statuses without the permission to also act on them.</summary>
    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    public List<ApplicationUser> Users { get; set; } = [];

    public PagerInfo Pager { get; set; } = null!;

    public Dictionary<Guid, IList<string>> RolesByUserId { get; set; } = [];

    public bool CanManageUserStatus { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>Direction a click on <paramref name="column"/>'s header should sort by next -
    /// toggles the current direction if it's already the active sort column, otherwise starts at
    /// that column's default (see <see cref="SortColumnDefaultDirection"/>). Used by the view to
    /// build each header's link.</summary>
    public string NextSortDir(string column) =>
        SortBy.Equals(column, StringComparison.OrdinalIgnoreCase)
            ? (SortDir == "asc" ? "desc" : "asc")
            : SortColumnDefaultDirection.GetValueOrDefault(column, "asc");

    /// <summary>Arrow to render next to a header, or empty if that column isn't the active sort.</summary>
    public string SortIndicator(string column) =>
        SortBy.Equals(column, StringComparison.OrdinalIgnoreCase)
            ? (SortDir == "asc" ? "▲" : "▼")
            : "";

    public async Task OnGetAsync()
    {
        PageSize = PagingExtensions.NormalizePageSize(PageSize, DefaultPageSize);
        if (!SortColumnDefaultDirection.ContainsKey(SortBy))
        {
            SortBy = "username";
        }
        SortDir = SortDir == "desc" ? "desc" : "asc";

        CanManageUserStatus = (
            await authorizationService.AuthorizeAsync(User, GamePermissions.ManageUserStatus)
        ).Succeeded;

        // Only High Members/Admins may filter by moderation status - anyone else's StatusFilter
        // is silently dropped rather than honored.
        if (!CanManageUserStatus || !StatusFilterRoles.ContainsKey(StatusFilter ?? ""))
        {
            StatusFilter = null;
        }

        // Deleted ("Took the Black") accounts have no reason to be exposed in a public directory.
        var query = userManager.Users.Where(u => !u.IsDeleted);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalized = Search.Trim();
            query = query.Where(u => EF.Functions.ILike(u.UserName!, $"%{normalized}%"));
        }
        if (StatusFilter is not null)
        {
            var roleName = StatusFilterRoles[StatusFilter];
            var userIdsInRole = dbContext
                .UserRoles.Join(
                    dbContext.Roles,
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => new { ur.UserId, r.Name }
                )
                .Where(x => x.Name == roleName)
                .Select(x => x.UserId);
            query = query.Where(u => userIdsInRole.Contains(u.Id));
        }

        // Every non-username column ties-break on username too, so paging stays stable/reproducible
        // even when many users share the same (e.g. 0) stat value. Null-valued stat columns
        // (no games recalculated yet, or a win rate that's never been defined because the user
        // has 0 finished games) are always pushed to the very end regardless of sort direction -
        // Postgres's default is NULLS FIRST for DESC, which would otherwise bury actual ranked
        // players under a wall of "n/a" accounts on page 1 whenever sorting a stats column
        // descending (the exact bug reported: sorting looked like it only affected the current
        // page, because the real top performers were pushed many pages deep).
        var ordered = (SortBy, SortDir) switch
        {
            ("finished", "desc") => query
                .OrderBy(u => u.CachedFinishedGamesCount == null)
                .ThenByDescending(u => u.CachedFinishedGamesCount)
                .ThenBy(u => u.UserName),
            ("finished", _) => query
                .OrderBy(u => u.CachedFinishedGamesCount == null)
                .ThenBy(u => u.CachedFinishedGamesCount)
                .ThenBy(u => u.UserName),
            ("won", "desc") => query
                .OrderBy(u => u.CachedWonGamesCount == null)
                .ThenByDescending(u => u.CachedWonGamesCount)
                .ThenBy(u => u.UserName),
            ("won", _) => query
                .OrderBy(u => u.CachedWonGamesCount == null)
                .ThenBy(u => u.CachedWonGamesCount)
                .ThenBy(u => u.UserName),
            ("removed", "desc") => query
                .OrderBy(u => u.CachedRemovedFromGameCount == null)
                .ThenByDescending(u => u.CachedRemovedFromGameCount)
                .ThenBy(u => u.UserName),
            ("removed", _) => query
                .OrderBy(u => u.CachedRemovedFromGameCount == null)
                .ThenBy(u => u.CachedRemovedFromGameCount)
                .ThenBy(u => u.UserName),
            ("winrate", "desc") => query
                .OrderBy(u => u.CachedWinRate == null)
                .ThenByDescending(u => u.CachedWinRate)
                .ThenBy(u => u.UserName),
            ("winrate", _) => query
                .OrderBy(u => u.CachedWinRate == null)
                .ThenBy(u => u.CachedWinRate)
                .ThenBy(u => u.UserName),
            ("created", "desc") => query.OrderByDescending(u => u.CreatedAt),
            ("created", _) => query.OrderBy(u => u.CreatedAt),
            ("activity", "desc") => query
                .OrderByDescending(u => u.LastActivity)
                .ThenBy(u => u.UserName),
            ("activity", _) => query.OrderBy(u => u.LastActivity).ThenBy(u => u.UserName),
            (_, "desc") => query.OrderByDescending(u => u.UserName),
            _ => query.OrderBy(u => u.UserName),
        };

        var paged = await ordered.ToPagedResultAsync(PageNumber, PageSize);
        Users = paged.Items;
        Pager = paged.Pager;

        foreach (var user in Users)
        {
            RolesByUserId[user.Id] = await userManager.GetRolesAsync(user);

            // Same "never compute inline, just enqueue" fallback as the individual profile page
            // (Pages.User.cshtml.cs) - a user whose stats have never been cached yet shows 0/n-a
            // for this one request and gets picked up by the background service instead.
            if (user.StatsCachedAt is null)
            {
                userStatsQueue.Enqueue(user.Id);
            }
        }
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(Guid id, string role)
    {
        if (
            !(
                await authorizationService.AuthorizeAsync(User, GamePermissions.ManageUserStatus)
            ).Succeeded
        )
        {
            return Forbid();
        }

        if (
            role != RoleNames.Banned
            && role != RoleNames.OnProbation
            && role != RoleNames.Tongueless
        )
        {
            return BadRequest();
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || user.IsDeleted)
        {
            return NotFound();
        }

        var existingRoles = await userManager.GetRolesAsync(user);
        if (existingRoles.Intersect(ProtectedRoles).Any())
        {
            // Refuse to touch Admins/High Members here, even if the caller crafted the request
            // manually - the UI never renders these buttons for them in the first place.
            StatusMessage = $"{user.UserName}'s status cannot be changed here.";
            return RedirectToPage(
                new
                {
                    Search,
                    PageNumber,
                    PageSize,
                    SortBy,
                    SortDir,
                    StatusFilter,
                }
            );
        }

        var roleLabel = role switch
        {
            RoleNames.OnProbation => "on probation",
            RoleNames.Tongueless => "tongueless",
            _ => "banned",
        };

        if (existingRoles.Contains(role))
        {
            await userManager.RemoveFromRoleAsync(user, role);
            StatusMessage = $"{user.UserName} is no longer {roleLabel}.";
        }
        else
        {
            await userManager.AddToRoleAsync(user, role);
            StatusMessage = $"{user.UserName} is now {roleLabel}.";
        }

        // Force the user out of any active session immediately, mirroring the Admin ban toggle and
        // the PlayApi banned check.
        await userManager.UpdateSecurityStampAsync(user);

        return RedirectToPage(
            new
            {
                Search,
                PageNumber,
                PageSize,
                SortBy,
                SortDir,
                StatusFilter,
            }
        );
    }
}
