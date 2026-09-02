using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace agot_bg_website.Areas.Admin.Pages.Users;

public class EditModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public ApplicationUser TargetUser { get; set; } = null!;

    public IList<string> CurrentRoles { get; set; } = [];

    public IReadOnlyList<string> AllRoles => RoleNames.All;

    [BindProperty]
    public List<string> SelectedRoles { get; set; } = [];

    /// <summary>
    /// Permissions granted directly to this user, on top of whatever their roles already grant
    /// (Django's <c>auth_user_user_permissions</c> one-off, per-user override) — see
    /// <see cref="Roles.EditModel"/> for the role-wide equivalent.
    /// </summary>
    public IReadOnlyList<string> AllPermissions => GamePermissions.All;

    [BindProperty]
    public List<string> SelectedPermissions { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        TargetUser = user;
        CurrentRoles = await userManager.GetRolesAsync(user);
        SelectedRoles = [.. CurrentRoles];

        var userClaims = await userManager.GetClaimsAsync(user);
        SelectedPermissions = [.. userClaims.Where(c => c.Type == GamePermissions.ClaimType).Select(c => c.Value)];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        TargetUser = user;
        var currentRoles = await userManager.GetRolesAsync(user);
        SelectedRoles ??= [];
        SelectedPermissions ??= [];

        var rolesToAdd = SelectedRoles.Except(currentRoles).ToArray();
        var rolesToRemove = currentRoles.Except(SelectedRoles).ToArray();

        if (rolesToAdd.Length > 0)
        {
            await userManager.AddToRolesAsync(user, rolesToAdd);
        }
        if (rolesToRemove.Length > 0)
        {
            await userManager.RemoveFromRolesAsync(user, rolesToRemove);
        }

        var currentUserClaims = await userManager.GetClaimsAsync(user);
        var currentPermissions = currentUserClaims
            .Where(c => c.Type == GamePermissions.ClaimType)
            .Select(c => c.Value)
            .ToArray();
        var permissionsToAdd = SelectedPermissions.Except(currentPermissions).ToArray();
        var permissionsToRemove = currentPermissions.Except(SelectedPermissions).ToArray();

        foreach (var permission in permissionsToAdd)
        {
            await userManager.AddClaimAsync(user, new System.Security.Claims.Claim(GamePermissions.ClaimType, permission));
        }
        foreach (var permission in permissionsToRemove)
        {
            await userManager.RemoveClaimAsync(user, new System.Security.Claims.Claim(GamePermissions.ClaimType, permission));
        }

        if (rolesToAdd.Length > 0 || rolesToRemove.Length > 0 || permissionsToAdd.Length > 0 || permissionsToRemove.Length > 0)
        {
            // Force role/permission changes (e.g. Banned/On probation, or a direct permission
            // grant) to take effect on the user's next request instead of only after their
            // session cookie naturally expires.
            await userManager.UpdateSecurityStampAsync(user);
        }

        StatusMessage = $"Roles and permissions for {user.UserName} updated.";
        return RedirectToPage("./Index");
    }
}
