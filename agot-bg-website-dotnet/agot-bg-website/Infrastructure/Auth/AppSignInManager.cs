using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Replaces the default <see cref="SignInManager{TUser}"/> registration (see Program.cs) purely to
/// add one more gate to <see cref="CanSignInAsync"/>: banned members. This mirrors Django's
/// <c>User.is_active = False</c>, which made <c>authenticate()</c> refuse the credentials outright
/// — see MIGRATION_PLAN.md's ban/redirect notes. <c>CanSignInAsync</c> is the single choke point
/// every sign-in path (password, external OAuth, 2FA) already funnels through via
/// <c>PreSignInCheck</c>, so overriding it here blocks login everywhere at once instead of having
/// to duplicate the check in every Account page. It complements, but does not replace, the
/// existing "force logout on next game-join" defense in Api/PlayApi.cs, which still matters for a
/// member who gets banned while their session/cookie is already active.
/// </summary>
public class AppSignInManager : SignInManager<ApplicationUser>
{
    public AppSignInManager(
        UserManager<ApplicationUser> userManager,
        Microsoft.AspNetCore.Http.IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        Microsoft.Extensions.Options.IOptions<IdentityOptions> optionsAccessor,
        Microsoft.Extensions.Logging.ILogger<SignInManager<ApplicationUser>> logger,
        Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!await base.CanSignInAsync(user))
        {
            return false;
        }

        return !await UserManager.IsInRoleAsync(user, RoleNames.Banned);
    }
}
