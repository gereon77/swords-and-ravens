using Microsoft.AspNetCore.Authorization;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Central registry of the site's custom game permissions — the ASP.NET Core equivalent of
/// Django's custom model permissions (<c>agotboardgame_main.models.Game.Meta.permissions</c>:
/// <c>can_play_as_another_player</c>, <c>cancel_game</c>, plus the default per-model
/// <c>add_game</c> permission Django generates automatically for every model).
///
/// Like Django, a permission's role/user assignment is *data*, not code: each permission is a
/// <c>"permission"</c>-typed claim (see <see cref="ClaimType"/>), stored using ASP.NET Core
/// Identity's own claims stores — <c>RoleManager&lt;IdentityRole&lt;Guid&gt;&gt;.AddClaimAsync</c>
/// (<c>AspNetRoleClaims</c>, the equivalent of Django's <c>auth_group_permissions</c>) for
/// role-wide grants, or <c>UserManager&lt;ApplicationUser&gt;.AddClaimAsync</c>
/// (<c>AspNetUserClaims</c>, Django's <c>auth_user_user_permissions</c>) for a one-off grant to a
/// single user. ASP.NET Core Identity automatically merges both into the signed-in user's
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> at sign-in
/// (<c>UserClaimsPrincipalFactory&lt;TUser,TRole&gt;.GenerateClaimsAsync</c> adds the user's own
/// claims plus every claim of every role they're in) — no custom claims-principal plumbing is
/// needed here. <see cref="PermissionSeeder"/> seeds the roles that have these permissions today
/// (Member/Admin/High Member) so behavior matches the legacy site out of the box; a future
/// Admin-area page can let admins edit these same role/user claims directly (add/remove a
/// "permission" claim on a role or user) with zero changes to this file or to the policies below.
///
/// Note: because permission claims are baked into the auth cookie at sign-in, a permission change
/// only takes effect for an already-signed-in user once their session is revalidated — bump
/// <c>UserManager.UpdateSecurityStampAsync(user)</c> for the affected user(s) after editing role
/// or user claims (see <c>Areas/Admin/Pages/Users/Edit.cshtml.cs</c> for the existing convention
/// of doing this after a role change) so it takes effect on their very next request rather than
/// only once the cookie's normal revalidation interval elapses.
///
/// Callers check a permission the same way any other ASP.NET Core authorization policy is checked
/// — via <see cref="IAuthorizationService.AuthorizeAsync(System.Security.Claims.ClaimsPrincipal, object?, string)"/>
/// (or a plain <c>[Authorize(Policy = ...)]</c> for MVC/minimal API endpoints) — never by
/// re-deriving a role list at the call site.
/// </summary>
public static class GamePermissions
{
    /// <summary>Claim type used for all "permission" claims granted to a role or a user.</summary>
    public const string ClaimType = "permission";

    /// <summary>Create a new game. Django's default "add_game" model permission.</summary>
    public const string CreateGame = "CreateGame";

    /// <summary>Impersonate another player in a game. Django's <c>can_play_as_another_player</c>.</summary>
    public const string ImpersonateOtherPlayers = "ImpersonateOtherPlayers";

    /// <summary>
    /// Directly write <c>Game.State = Cancelled</c> to the database, bypassing the game server.
    /// Django's <c>cancel_game</c>.
    /// </summary>
    public const string CancelGame = "CancelGame";

    /// <summary>
    /// All known permission values, for admin UI (<c>Areas/Admin/Pages/Roles</c> and
    /// <c>Areas/Admin/Pages/Users/Edit</c>) to render as a checkbox list.
    /// </summary>
    public static readonly string[] All = [CreateGame, ImpersonateOtherPlayers, CancelGame];

    public static AuthorizationBuilder AddGamePermissionPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(CreateGame, policy => policy.RequireAssertion(context =>
            context.User.Identity?.IsAuthenticated == true &&
            !context.User.IsInRole(RoleNames.Banned) &&
            !context.User.IsInRole(RoleNames.OnProbation) &&
            context.User.HasClaim(ClaimType, CreateGame)))
        .AddPolicy(ImpersonateOtherPlayers, policy => policy.RequireClaim(ClaimType, ImpersonateOtherPlayers))
        .AddPolicy(CancelGame, policy => policy.RequireClaim(ClaimType, CancelGame));
}
