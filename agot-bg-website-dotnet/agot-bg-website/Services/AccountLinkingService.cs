using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Services;

/// <summary>
/// Result of trying to link an external login to an existing account. See MIGRATION_PLAN.md §5.3.
/// </summary>
public enum AccountLinkOutcome
{
    /// <summary>No matching imported-and-unclaimed user found — caller should create a brand new user.</summary>
    NoMatch,

    /// <summary>Successfully linked the external login to a previously-imported, unclaimed legacy user.</summary>
    Linked,

    /// <summary>A user with this email exists but is already claimed by someone else — do not auto-merge.</summary>
    ConflictAlreadyClaimed
}

public record AccountLinkResult(AccountLinkOutcome Outcome, ApplicationUser? User);

/// <summary>
/// Mirrors Django's `social_core.pipeline.social_auth.associate_by_email`: when a new external
/// login arrives with an email that matches an ImportedFromLegacy-but-unclaimed user, link to that
/// user and flip Claimed permanently. Never re-links or silently merges into an already-claimed
/// account (that would be an account-takeover risk) — see MIGRATION_PLAN.md §5.3.
/// </summary>
public class AccountLinkingService(UserManager<ApplicationUser> userManager)
{
    public async Task<AccountLinkResult> TryLinkByEmailAsync(string normalizedEmail)
    {
        var existing = await userManager.Users
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .ToListAsync();

        if (existing.Count == 0)
        {
            return new AccountLinkResult(AccountLinkOutcome.NoMatch, null);
        }

        // Prefer an already-claimed exact match first (a returning user logging in again with the
        // same provider, or a second provider on top of an already-claimed account).
        var claimed = existing.FirstOrDefault(u => u.Claimed);
        if (claimed is not null)
        {
            return new AccountLinkResult(AccountLinkOutcome.ConflictAlreadyClaimed, claimed);
        }

        var unclaimedImported = existing.FirstOrDefault(u => u.ImportedFromLegacy && !u.Claimed);
        if (unclaimedImported is null)
        {
            return new AccountLinkResult(AccountLinkOutcome.NoMatch, null);
        }

        unclaimedImported.Claimed = true;
        unclaimedImported.EmailConfirmed = true;
        await userManager.UpdateAsync(unclaimedImported);

        return new AccountLinkResult(AccountLinkOutcome.Linked, unclaimedImported);
    }
}
