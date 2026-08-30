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
    IAuthorizationService authorizationService
) : PageModel
{
    private const int DefaultPageSize = 10;

    /// <summary>Roles a moderator is never allowed to alter here, to prevent High Members from
    /// banning/tonguing each other or Admins - only plain Members can be moderated this way.</summary>
    private static readonly string[] ProtectedRoles = [RoleNames.Admin, RoleNames.HighMember];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    public List<ApplicationUser> Users { get; set; } = [];

    public PagerInfo Pager { get; set; } = null!;

    public Dictionary<Guid, IList<string>> RolesByUserId { get; set; } = [];

    public bool CanManageUserStatus { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        PageSize = PagingExtensions.NormalizePageSize(PageSize, DefaultPageSize);

        CanManageUserStatus = (
            await authorizationService.AuthorizeAsync(User, GamePermissions.ManageUserStatus)
        ).Succeeded;

        // Deleted ("Took the Black") accounts have no reason to be exposed in a public directory.
        var query = userManager.Users.Where(u => !u.IsDeleted);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalized = Search.Trim();
            query = query.Where(u => EF.Functions.ILike(u.UserName!, $"%{normalized}%"));
        }

        var paged = await query.OrderBy(u => u.UserName).ToPagedResultAsync(PageNumber, PageSize);
        Users = paged.Items;
        Pager = paged.Pager;

        foreach (var user in Users)
        {
            RolesByUserId[user.Id] = await userManager.GetRolesAsync(user);
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
            }
        );
    }
}
