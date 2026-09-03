using Microsoft.AspNetCore.Identity;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Grants the default role → permission mapping the legacy Django site had (Django's
/// <c>auth_group_permissions</c> table, populated by hand via the Django admin) as
/// <see cref="GamePermissions.ClaimType"/> claims on the built-in roles, via
/// <c>RoleManager.AddClaimAsync</c> (<c>AspNetRoleClaims</c>). Idempotent — safe to run on every
/// startup, same as <see cref="RoleSeeder"/>.
///
/// Because these are ordinary Identity role claims, they can be freely edited afterwards — by a
/// future Admin-area page calling <c>RoleManager.AddClaimAsync</c>/<c>RemoveClaimAsync</c> for a
/// role, or <c>UserManager.AddClaimAsync</c>/<c>RemoveClaimAsync</c> for a one-off grant to a
/// single user — with zero changes needed here. Any *extra* permission an admin grants beyond
/// this default set is left untouched by later runs. However, exactly like <see cref="RoleSeeder"/>
/// always recreating a deleted built-in role, this seeder always re-ensures its default set is
/// present on every run — so a default permission an admin deliberately revokes from one of these
/// three roles will reappear the next time the app restarts. If per-role overrides of the default
/// set ever need to survive a restart, this seeder will need a persisted "already seeded" marker
/// instead of unconditionally re-checking the default list every time.
/// </summary>
public static class PermissionSeeder
{
    /// <summary>
    /// The default permission set for each role that Django's legacy group→permission
    /// assignments granted. Member/Admin/High Member can all create games (Django's default
    /// <c>add_game</c> permission was granted to every non-banned/non-probation member); Admin and
    /// High Member can additionally impersonate other players and cancel games directly (Django's
    /// <c>can_play_as_another_player</c>/<c>cancel_game</c> permissions), and moderate other
    /// players' On probation/Tongueless/Banned status from the public Users directory (new -
    /// Django delegated this entirely to staff via the Django admin instead).
    /// </summary>
    private static readonly Dictionary<string, string[]> DefaultRolePermissions = new()
    {
        [RoleNames.Member] = [GamePermissions.CreateGame],
        [RoleNames.Admin] =
        [
            GamePermissions.CreateGame,
            GamePermissions.ImpersonateOtherPlayers,
            GamePermissions.CancelGame,
            GamePermissions.ManageUserStatus,
        ],
        [RoleNames.HighMember] =
        [
            GamePermissions.CreateGame,
            GamePermissions.ImpersonateOtherPlayers,
            GamePermissions.CancelGame,
            GamePermissions.ManageUserStatus,
        ],
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var (roleName, permissions) in DefaultRolePermissions)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                // RoleSeeder should always run first and create every role in RoleNames.All, but
                // don't blow up startup if it hasn't (e.g. a database not yet fully seeded).
                continue;
            }

            var existingClaims = await roleManager.GetClaimsAsync(role);
            var existingPermissions = existingClaims
                .Where(c => c.Type == GamePermissions.ClaimType)
                .Select(c => c.Value)
                .ToHashSet();

            foreach (var permission in permissions.Where(p => !existingPermissions.Contains(p)))
            {
                await roleManager.AddClaimAsync(
                    role,
                    new System.Security.Claims.Claim(GamePermissions.ClaimType, permission)
                );
            }
        }
    }
}
