namespace agot_bg_website.Infrastructure.Paging;

/// <summary>
/// Persists the "/Users" directory page's current page number and sort column/direction so a
/// returning visitor sees their previous view instead of always restarting at page 1 sorted by
/// username. Search text and the moderation StatusFilter are deliberately NOT persisted here -
/// only the page/sort position, matching what was asked for.
///
/// Unlike <see cref="PageSizeCookie"/> (read server-side, but only ever written client-side via
/// site.js/inline scripts to sidestep the GDPR cookie-consent gate - see
/// <c>Program.cs</c>'s <c>CookiePolicyOptions.CheckConsentNeeded</c>), this cookie is written
/// directly server-side and marked <see cref="CookieOptions.IsEssential"/>, the same way the
/// authentication cookie already is. That's considered acceptable here because it's a strictly
/// functional UI preference with no tracking/marketing purpose, and doing it server-side avoids
/// having to duplicate "which link/form just navigated" detection in JS for every sort-header
/// link, pager link, and "go to page" form on this particular page.
/// </summary>
public static class UsersListPreferencesCookie
{
    private const string CookieName = "snr_users_list_prefs";

    public static void Persist(
        HttpResponse response,
        int pageNumber,
        string sortBy,
        string sortDir
    ) =>
        response.Cookies.Append(
            CookieName,
            $"{pageNumber}|{sortBy}|{sortDir}",
            new CookieOptions
            {
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
            }
        );

    /// <summary>
    /// The last-persisted (page number, sort column, sort direction), or null if nothing was ever
    /// saved (or the cookie is malformed/tampered with). Sort column/direction are returned as-is
    /// without validating them against <see cref="UsersModel"/>'s allowed values - callers already
    /// re-validate SortBy/SortDir the same way they do for an explicit querystring value.
    /// </summary>
    public static (int PageNumber, string SortBy, string SortDir)? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var raw))
        {
            return null;
        }

        var parts = raw.Split('|');
        return parts.Length == 3 && int.TryParse(parts[0], out var pageNumber) && pageNumber >= 1
            ? (pageNumber, parts[1], parts[2])
            : null;
    }
}
