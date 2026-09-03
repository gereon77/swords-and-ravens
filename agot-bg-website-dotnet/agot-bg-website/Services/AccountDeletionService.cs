using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity;

namespace agot_bg_website.Services;

/// <summary>
/// Implements the "Took the Black" soft-delete described in MIGRATION_PLAN.md §13. We never hard-
/// delete an AspNetUsers row or move it to a separate table: PlayerInGame, PreviousPlayerInGame and
/// Message all reference UserId with ON DELETE RESTRICT so historical games/chat logs keep loading
/// correctly. Instead we strip every piece of PII from the row in place (UserName becomes the
/// user's own Id - already unique, not PII) and flip <see cref="ApplicationUser.IsDeleted"/>;
/// <see cref="ApplicationUser.DisplayName"/> is what makes every deleted account show up as
/// "Took the Black" in the UI.
/// </summary>
public class AccountDeletionService(
    UserManager<ApplicationUser> userManager,
    ILogger<AccountDeletionService> logger
)
{
    public async Task<IdentityResult> DeleteAccountAsync(ApplicationUser user)
    {
        if (user.IsDeleted)
        {
            return IdentityResult.Success;
        }

        // Remove roles, external logins, claims and 2FA/recovery tokens up front - RemoveXAsync
        // calls below hit the store directly rather than relying on a later UpdateAsync.
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, roles);
        }

        var logins = await userManager.GetLoginsAsync(user);
        foreach (var login in logins)
        {
            await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        }

        var claims = await userManager.GetClaimsAsync(user);
        if (claims.Count > 0)
        {
            await userManager.RemoveClaimsAsync(user, claims);
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            await userManager.SetTwoFactorEnabledAsync(user, false);
        }

        // Strip PII. Email/UserName are nullable *columns*, but Identity's default UserValidator
        // rejects a null/empty UserName outright (regardless of RequireUniqueEmail) and still
        // enforces its own uniqueness check against whatever we set - so we can't literally reuse
        // "Took the Black" for every deleted row. The user's own (already-unique, non-PII) Id
        // makes a perfectly good hidden placeholder; ApplicationUser.DisplayName is what actually
        // shows "Took the Black" everywhere the UI displays a name.
        user.UserName = user.Id.ToString();
        user.NormalizedUserName = user.UserName.ToUpperInvariant();

        // Can't null Email out either: Identity's default UserValidator rejects a null/empty
        // email outright whenever options.User.RequireUniqueEmail is true (see Program.cs),
        // regardless of the column itself being nullable. ".invalid" is the RFC 2606 TLD
        // reserved for exactly this - a syntactically valid but guaranteed-undeliverable address.
        user.Email = $"{user.Id:N}@deleted.invalid";
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.EmailConfirmed = false;
        user.EmailNotificationActive = false;
        user.PasswordHash = null;
        user.ProfileText = null;
        user.LastWonTournament = null;

        // GameToken is NOT NULL + UNIQUE, so it can't be nulled out - replace it with a fresh,
        // never-handed-out value instead of leaving the old (now orphaned) one usable.
        user.GameToken = Guid.NewGuid().ToString("N");

        // Belt-and-braces: lock the account out even though the password hash is already gone,
        // and rotate the security stamp so any existing auth cookie is invalidated immediately.
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;

        user.IsDeleted = true;
        user.DeletedAt = DateTimeOffset.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (result.Succeeded)
        {
            await userManager.UpdateSecurityStampAsync(user);
            logger.LogInformation("User {UserId} soft-deleted ('Took the Black').", user.Id);
        }
        else
        {
            logger.LogError(
                "Soft-deleting user {UserId} failed: {Errors}",
                user.Id,
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        return result;
    }
}
