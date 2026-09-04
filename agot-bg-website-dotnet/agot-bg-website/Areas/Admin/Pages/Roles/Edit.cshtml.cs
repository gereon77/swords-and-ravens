using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace agot_bg_website.Areas.Admin.Pages.Roles;

public class EditModel(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager
) : PageModel
{
    [BindProperty]
    public string RoleName { get; set; } = "";

    public IReadOnlyList<string> AllPermissions => GamePermissions.All;

    [BindProperty]
    public List<string> SelectedPermissions { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string roleName)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            return NotFound();
        }

        RoleName = roleName;
        var claims = await roleManager.GetClaimsAsync(role);
        SelectedPermissions =
        [
            .. claims.Where(c => c.Type == GamePermissions.ClaimType).Select(c => c.Value),
        ];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string roleName)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            return NotFound();
        }

        RoleName = roleName;
        SelectedPermissions ??= [];

        var currentClaims = await roleManager.GetClaimsAsync(role);
        var currentPermissions = currentClaims
            .Where(c => c.Type == GamePermissions.ClaimType)
            .Select(c => c.Value)
            .ToArray();

        var toAdd = SelectedPermissions.Except(currentPermissions).ToArray();
        var toRemove = currentPermissions.Except(SelectedPermissions).ToArray();

        foreach (var permission in toAdd)
        {
            await roleManager.AddClaimAsync(
                role,
                new System.Security.Claims.Claim(GamePermissions.ClaimType, permission)
            );
        }
        foreach (var permission in toRemove)
        {
            await roleManager.RemoveClaimAsync(
                role,
                new System.Security.Claims.Claim(GamePermissions.ClaimType, permission)
            );
        }

        if (toAdd.Length > 0 || toRemove.Length > 0)
        {
            // Same reasoning as Users/Edit.cshtml.cs bumping a single user's security stamp after
            // a role change: a role-wide permission claim change only takes effect for an
            // already-signed-in member of this role once their session is revalidated, so force
            // it on their very next request instead.
            var usersInRole = await userManager.GetUsersInRoleAsync(roleName);
            foreach (var user in usersInRole)
            {
                await userManager.UpdateSecurityStampAsync(user);
            }
        }

        StatusMessage = $"Permissions for role {roleName} updated.";
        return RedirectToPage("./Index");
    }
}
