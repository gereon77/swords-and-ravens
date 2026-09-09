using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace agot_bg_website.Areas.Admin.Pages.Roles;

public class IndexModel(RoleManager<IdentityRole<Guid>> roleManager) : PageModel
{
    public record RoleRow(string Name, IReadOnlyList<string> Permissions);

    public List<RoleRow> Roles { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        foreach (var roleName in RoleNames.All)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                Roles.Add(new RoleRow(roleName, []));
                continue;
            }

            var claims = await roleManager.GetClaimsAsync(role);
            var permissions = claims
                .Where(c => c.Type == GamePermissions.ClaimType)
                .Select(c => c.Value)
                .ToArray();
            Roles.Add(new RoleRow(roleName, permissions));
        }
    }
}
