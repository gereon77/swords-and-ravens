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

        var toAdd = SelectedRoles.Except(currentRoles).ToArray();
        var toRemove = currentRoles.Except(SelectedRoles).ToArray();

        if (toAdd.Length > 0)
        {
            await userManager.AddToRolesAsync(user, toAdd);
        }
        if (toRemove.Length > 0)
        {
            await userManager.RemoveFromRolesAsync(user, toRemove);
        }
        if (toAdd.Length > 0 || toRemove.Length > 0)
        {
            // Force role changes (e.g. Banned/On probation) to take effect on the user's next
            // request instead of only after their session cookie naturally expires.
            await userManager.UpdateSecurityStampAsync(user);
        }

        StatusMessage = $"Roles for {user.UserName} updated.";
        return RedirectToPage("./Index");
    }
}
