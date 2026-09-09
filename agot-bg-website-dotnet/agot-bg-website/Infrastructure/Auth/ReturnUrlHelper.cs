namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Normalizes the post-login return URL. Visitors almost always click "Login" because they want
/// to get to the Games list — having already seen the marketing home page before logging in,
/// landing back on it after signing in is a pointless extra click every single time. Any other
/// return URL (e.g. a deep link the [Authorize] challenge captured, or a specific game page) is
/// left untouched so "redirect back to what I was trying to reach" still works as expected.
/// </summary>
public static class ReturnUrlHelper
{
    public const string DefaultAuthenticatedLandingPage = "/Games";

    public static string NormalizeAfterLogin(string? returnUrl)
    {
        return string.IsNullOrEmpty(returnUrl) || IsHomePage(returnUrl)
            ? DefaultAuthenticatedLandingPage
            : returnUrl;
    }

    private static bool IsHomePage(string returnUrl)
    {
        var path = returnUrl.Split('?', '#')[0].TrimEnd('/');
        return path.Length == 0 || path.Equals("/Index", StringComparison.OrdinalIgnoreCase);
    }
}
