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
}
