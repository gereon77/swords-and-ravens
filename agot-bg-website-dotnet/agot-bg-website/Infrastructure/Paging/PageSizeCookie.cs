namespace agot_bg_website.Infrastructure.Paging;

/// <summary>
/// Reads the persisted "items per page" preference for an admin list page (Games/Rooms/Messages/
/// Users) from a per-path cookie, keyed so each list page remembers its own preferred page size
/// independently. The cookie is written client-side (see the inline script in
/// <c>Pages/Shared/_Pager.cshtml</c>) whenever the "Items per page" form is submitted, using the
/// exact cookie name this class computes so client and server never disagree on it. The server
/// then bakes the persisted value directly into the page-size query-string binding instead of an
/// earlier version of this feature that stored the value in localStorage and had to redirect
/// client-side after first paint whenever the rendered default didn't match - see
/// CollapseStateExtensions for the sibling fix (details-section open/closed state) that
/// motivated moving this from localStorage to a server-readable cookie too.
/// </summary>
public static class PageSizeCookie
{
    private const string CookiePrefix = "snr_pagesize_";

    /// <summary>
    /// Cookie name for <paramref name="path"/>. Cookie <em>names</em> are an HTTP token (RFC
    /// 6265) and may not contain characters like ':' or '/' - a request path such as
    /// "/Areas/Admin/Games" is reduced to alphanumerics-and-hyphens first, the same fix that was
    /// needed for CollapseStateExtensions's colon-separated keys, which .NET's cookie header
    /// parser was silently dropping.
    /// </summary>
    public static string KeyFor(PathString path) => CookiePrefix + Sanitize(path.Value);

    private static string Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "root";
        }

        var sanitized = new string(
            raw.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()
        ).Trim('-');
        return sanitized.Length == 0 ? "root" : sanitized;
    }

    /// <summary>
    /// The persisted page size for <paramref name="request"/>'s path, or
    /// <paramref name="defaultPageSize"/> if no cookie was ever saved (or it doesn't parse as a
    /// positive number). Callers should only fall back to this when the current request's
    /// query string doesn't already specify a <c>pageSize</c> explicitly, so an explicit
    /// querystring value (e.g. from a bookmarked link) always wins over a previously saved
    /// preference.
    /// </summary>
    public static int Read(HttpRequest request, int defaultPageSize) =>
        request.Cookies.TryGetValue(KeyFor(request.Path), out var raw)
        && int.TryParse(raw, out var value)
            ? PagingExtensions.NormalizePageSize(value, defaultPageSize)
            : defaultPageSize;
}
