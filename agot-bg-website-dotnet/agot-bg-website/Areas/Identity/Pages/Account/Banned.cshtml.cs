using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace agot_bg_website.Areas.Identity.Pages.Account;

/// <summary>
/// Shown instead of letting a banned member log in - see AppSignInManager.CanSignInAsync and
/// MIGRATION_PLAN.md's ban/redirect notes. [AllowAnonymous] because the whole point is that the
/// visitor was refused a session, so they're never authenticated when they land here.
/// </summary>
[AllowAnonymous]
public class BannedModel : PageModel
{
    public void OnGet() { }
}
