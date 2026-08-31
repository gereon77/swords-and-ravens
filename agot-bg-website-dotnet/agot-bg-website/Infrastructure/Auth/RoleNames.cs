namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Role names, mirroring Django's <c>auth_group</c> rows used for authorization today
/// (<c>agotboardgame_main.User.is_in_group</c>) — see MIGRATION_PLAN.md §4.2/§5. Snr.Migration
/// imports the legacy groups of the same names, so these must stay in sync with that importer.
/// </summary>
public static class RoleNames
{
    public const string Member = "Member";
    public const string Admin = "Admin";
    public const string HighMember = "High Member";
    public const string Banned = "Banned";
    public const string OnProbation = "On probation";
    public const string Tongueless = "Tongueless";

    public static readonly string[] All = [Member, Admin, HighMember, Banned, OnProbation, Tongueless];

    /// <summary>
    /// Roles allowed to impersonate another player in a game — equivalent of Django's
    /// <c>agotboardgame_main.can_play_as_another_player</c> permission, which in the legacy site
    /// is only ever granted to admins/moderators.
    /// </summary>
    public static readonly string[] CanPlayAsAnotherPlayer = [Admin, HighMember];

    /// <summary>
    /// Checks whether a user has permission to create a new game (must be authenticated, have
    /// Member, Admin, or High Member role, and must NOT be Banned or On probation).
    /// </summary>
    public static bool CanCreateGame(System.Security.Claims.ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (user.IsInRole(Banned) || user.IsInRole(OnProbation))
        {
            return false;
        }

        return user.IsInRole(Member) || user.IsInRole(Admin) || user.IsInRole(HighMember);
    }
}
