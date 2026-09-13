namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Single source of truth for which characters a username may contain, shared by the three places
/// a user picks/changes their own name (Register.cshtml.cs, ExternalLogin.cshtml.cs,
/// Manage/Index.cshtml.cs's <c>[RegularExpression]</c> attributes) and by
/// <see cref="SafeUserValidator"/> (which re-checks every existing username on every
/// UserManager.CreateAsync/UpdateAsync call, including ones that don't touch the name at all -
/// e.g. linking a new external login onto a legacy-imported account).
///
/// Uses Unicode category classes (<c>\p{L}</c> letters, <c>\p{Nd}</c> decimal digits) rather than
/// <c>a-zA-Z0-9</c> so names with accents/diacritics (e.g. "Länsiauto", "LuízaWD" - both real,
/// legitimately imported legacy usernames) are accepted, not just ASCII. This deliberately still
/// excludes everything outside those two categories - combining marks, zero-width/invisible
/// characters, and bidi/RTL-override control characters are not in <c>\p{L}</c>/<c>\p{Nd}</c> and
/// so remain rejected.
/// </summary>
public static class UsernamePolicy
{
    public const string Pattern = @"^[\p{L}\p{Nd}_\-\. ]+$";

    public const string ErrorMessage =
        "Username can only contain letters, numbers, spaces, dots, underscores, and dashes.";
}
