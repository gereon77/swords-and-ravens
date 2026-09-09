namespace agot_bg_website.Infrastructure;

/// <summary>
/// Reads the persisted open/closed state of a collapsible <c>&lt;details&gt;</c> "collapse"
/// section on Games/MyGames/User (see site.js, which writes one cookie per
/// <c>data-collapse-key</c> whenever a section is toggled). The server bakes the correct `open`
/// attribute directly into the initial HTML render from this cookie, instead of a client-side
/// script correcting it after first paint - that earlier approach caused a visible layout
/// shift/jank on every load once a user had ever collapsed a section (the section would render
/// open, then immediately snap shut once the script ran), which is what this replaces.
/// </summary>
public static class CollapseStateExtensions
{
    private const string CookiePrefix = "snr_collapse_";

    public static bool IsCollapseSectionOpen(
        this HttpRequest request,
        string key,
        bool defaultOpen
    ) =>
        request.Cookies.TryGetValue(CookiePrefix + key, out var value)
            ? value == "open"
            : defaultOpen;
}
