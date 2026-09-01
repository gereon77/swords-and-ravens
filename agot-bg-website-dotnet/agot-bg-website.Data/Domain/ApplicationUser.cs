using Microsoft.AspNetCore.Identity;

namespace agot_bg_website.Domain;

/// <summary>
/// Extended Identity user. Mirrors the fields Django's agotboardgame_main.User carries today,
/// see MIGRATION_PLAN.md §4.2. Uses a Guid key so imported legacy user ids can be preserved
/// exactly (they are embedded inside game-server serialized_game/view_of_game JSON).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// We never use phone-number verification and the columns were dropped by the
    /// RemovePhoneNumberFields migration (see ApplicationDbContext's Ignore() calls). Reflection-based
    /// tooling (CoreAdmin's grid generator, Identity's scaffolded "Download personal data" page) reads
    /// CLR properties directly instead of respecting EF's Ignore() mapping or attribute inheritance, so
    /// simply ignoring/overriding the inherited IdentityUser members is not enough to hide them there.
    /// Shadowing with an internal "new" property removes them entirely from public reflection
    /// (GetProperties()) while leaving the still-virtual base members untouched for anything that
    /// references IdentityUser&lt;Guid&gt; directly (e.g. EF Core's UserStore). Internal (rather than
    /// private) so ApplicationDbContext's Ignore() calls below can still reference them by name.
    /// </summary>
    internal new string? PhoneNumber { get; set; }
    internal new bool PhoneNumberConfirmed { get; set; }

    [PersonalData]
    /// <summary>Bearer token the game server uses to authenticate as this user (~ Django game_token).</summary>
    public string GameToken { get; set; } = Guid.NewGuid().ToString("N");

    [PersonalData]
    public string? ProfileText { get; set; }

    [PersonalData]
    public string? LastWonTournament { get; set; }

    public bool EmailNotificationActive { get; set; } = true;

    public bool MuteGames { get; set; }

    public bool UseHouseNamesForChat { get; set; }

    public bool UseMapScrollbar { get; set; } = true;

    public bool UseResponsiveLayoutOnMobile { get; set; }

    [PersonalData]
    public DateTimeOffset? LastUsernameUpdateTime { get; set; }

    [PersonalData]
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Kept only in case the (currently dead) Vanilla Forum integration is ever revived.</summary>
    public int VanillaForumUserId { get; set; }

    /// <summary>True for rows created by the Snr.Migration importer from the legacy Django database.</summary>
    public bool ImportedFromLegacy { get; set; }

    /// <summary>False only for ImportedFromLegacy rows that have not yet been claimed via a real login.</summary>
    public bool Claimed { get; set; } = true;

    [PersonalData]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Soft-delete flag ("Took the Black"). We deliberately keep the AspNetUsers row instead of
    /// hard-deleting or moving it to a separate table: PlayerInGame/PreviousPlayerInGame/Message
    /// all reference UserId with ON DELETE RESTRICT, so a real delete would either be blocked or
    /// require rewriting every historical game/chat row. Instead AccountDeletionService strips all
    /// PII from this row in place (UserName becomes the user's own Id - Identity's UserValidator
    /// rejects null/duplicate usernames, and the Id is already unique and not PII) and flips this
    /// flag. DisplayName below is what actually shows "Took the Black" to users. See MIGRATION_PLAN.md §13.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Name to show anywhere a username would normally be displayed (games, chat, admin).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => IsDeleted ? "Took the Black" : (UserName ?? "Unknown");
}
