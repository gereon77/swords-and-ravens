using System.ComponentModel.DataAnnotations;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Replaces the default <see cref="UserValidator{TUser}"/> registration (see Program.cs) because
/// its private ValidateUserName/ValidateEmail helpers call
/// <see cref="UserManager{TUser}.FindByNameAsync"/>/<see cref="UserManager{TUser}.FindByEmailAsync"/>,
/// both of which use EF Core's SingleOrDefaultAsync internally and throw
/// <see cref="InvalidOperationException"/> ("Sequence contains more than one element") instead of
/// returning a clean result whenever more than one row already shares that normalized name/email -
/// which several legacy-imported rows do (many share the same blank NormalizedEmail, see
/// Migrations/20260909183034_FixDuplicateEmptyEmails.cs). Since ValidateUserName/ValidateEmail are
/// private (not virtual), they can't be fixed by subclassing UserValidator&lt;TUser&gt; - this
/// reimplements the same rules (same messages, via the same IdentityErrorDescriber) but checks
/// uniqueness by counting matches instead of relying on a single result, so it can never crash this
/// way regardless of what duplicate data exists. This runs for every UserManager.CreateAsync/
/// UpdateAsync call - covering registration, profile edits, Ban/Unban and Edit-roles (which go
/// through RemoveFromRolesAsync/AddToRolesAsync -> UpdateUserAsync -> ValidateUserAsync), and
/// AccountDeletionService's soft-delete flow - not just one call site.
/// </summary>
public class SafeUserValidator(IdentityErrorDescriber? describer = null)
    : IUserValidator<ApplicationUser>
{
    private readonly IdentityErrorDescriber _describer = describer ?? new IdentityErrorDescriber();

    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user
    )
    {
        var errors = new List<IdentityError>();

        await ValidateUserNameAsync(manager, user, errors);
        if (manager.Options.User.RequireUniqueEmail)
        {
            await ValidateEmailAsync(manager, user, errors);
        }

        return errors.Count > 0 ? IdentityResult.Failed([.. errors]) : IdentityResult.Success;
    }

    private async Task ValidateUserNameAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        List<IdentityError> errors
    )
    {
        var userName = await manager.GetUserNameAsync(user);
        if (string.IsNullOrWhiteSpace(userName))
        {
            errors.Add(_describer.InvalidUserName(userName));
            return;
        }

        if (
            !string.IsNullOrEmpty(manager.Options.User.AllowedUserNameCharacters)
            && userName.Any(c => !manager.Options.User.AllowedUserNameCharacters.Contains(c))
        )
        {
            errors.Add(_describer.InvalidUserName(userName));
            return;
        }

        var normalizedUserName = manager.NormalizeName(userName);
        var otherOwnerExists = await manager
            .Users.Where(u => u.NormalizedUserName == normalizedUserName && u.Id != user.Id)
            .AnyAsync();
        if (otherOwnerExists)
        {
            errors.Add(_describer.DuplicateUserName(userName));
        }
    }

    private async Task ValidateEmailAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        List<IdentityError> errors
    )
    {
        var email = await manager.GetEmailAsync(user);
        if (string.IsNullOrWhiteSpace(email))
        {
            errors.Add(_describer.InvalidEmail(email));
            return;
        }

        if (!new EmailAddressAttribute().IsValid(email))
        {
            errors.Add(_describer.InvalidEmail(email));
            return;
        }

        var normalizedEmail = manager.NormalizeEmail(email);
        var otherOwnerExists = await manager
            .Users.Where(u => u.NormalizedEmail == normalizedEmail && u.Id != user.Id)
            .AnyAsync();
        if (otherOwnerExists)
        {
            errors.Add(_describer.DuplicateEmail(email));
        }
    }
}
