namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Badge color per role, mirroring Django's settings.GROUP_COLORS (bootstrap contextual names
/// mapped onto their DaisyUI badge-* equivalents). Shared between the public profile page
/// (<c>Pages/User.cshtml.cs</c>) and the public users directory (<c>Pages/Users.cshtml.cs</c>) so
/// both render role badges identically.
/// </summary>
public static class RoleBadges
{
    public static readonly IReadOnlyDictionary<string, string> Classes = new Dictionary<
        string,
        string
    >
    {
        [RoleNames.Admin] = "badge-error",
        [RoleNames.HighMember] = "badge-info",
        [RoleNames.Banned] = "badge-error",
        [RoleNames.OnProbation] = "badge-warning",
        [RoleNames.Tongueless] = "badge-warning",
    };
}
