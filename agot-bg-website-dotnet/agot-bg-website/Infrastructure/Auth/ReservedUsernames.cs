using agot_bg_website.Domain;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Usernames nobody may register or rename to, because they'd be visually indistinguishable from a
/// status the site itself displays next to a name: either a role badge (see
/// Pages/User.cshtml.cs's <c>RoleBadgeClasses</c>, which renders these exact strings) or the fixed
/// <see cref="ApplicationUser.DeletedAccountDisplayName"/> label shown for every soft-deleted
/// account. <see cref="IsReserved"/> is checked from both Register.cshtml.cs (new account) and
/// Manage/Index.cshtml.cs (the one-time username change) - there's nowhere else a user picks their
/// own username. Deliberately not an exhaustive list (e.g. "Member"/"On probation"/"Tongueless"
/// were considered and dropped as not worth the false-positive risk) - extend it here as more
/// names worth blocking come up.
///
/// Comparison is case-insensitive and ignores extra/leading/trailing whitespace, since usernames
/// are otherwise allowed to contain spaces (see Register's <c>UserName</c>
/// <c>RegularExpression</c>). Any single space-separated word that exactly matches a single-word
/// reserved name is enough to reject the whole username (e.g. "John Admin" is blocked because
/// "Admin" is one of its words, not just an exact-whole-name match) - the multi-word entries
/// ("Took the Black", "High Member") additionally need a whole-username match since no single word
/// equals one of those phrases on its own.
/// </summary>
public static class ReservedUsernames
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationUser.DeletedAccountDisplayName,
        RoleNames.Admin,
        "Administrator",
        RoleNames.HighMember,
        "Moderator",
        RoleNames.Banned,
    };

    public static bool IsReserved(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var words = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(Names.Contains) || Names.Contains(string.Join(' ', words));
    }
}
