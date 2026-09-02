using agot_bg_website.Infrastructure.Auth;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

/// <summary>
/// Covers "after login, land on Games instead of bouncing back to the home page you already saw"
/// (see MIGRATION_PLAN.md's ban/redirect notes and Login.cshtml.cs/ExternalLogin.cshtml.cs, which
/// both funnel their returnUrl through this helper).
/// </summary>
public class ReturnUrlHelperTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/Index")]
    [InlineData("/index")]
    [InlineData("/Index?foo=bar")]
    [InlineData("/Index#section")]
    public void NormalizeAfterLogin_HomePageOrEmpty_RedirectsToGames(string? returnUrl)
    {
        Assert.Equal("/Games", ReturnUrlHelper.NormalizeAfterLogin(returnUrl));
    }

    [Theory]
    [InlineData("/Games")]
    [InlineData("/MyGames")]
    [InlineData("/User/00000000-0000-0000-0000-000000000000")]
    [InlineData("/Rules")]
    public void NormalizeAfterLogin_OtherPage_LeavesReturnUrlUntouched(string returnUrl)
    {
        Assert.Equal(returnUrl, ReturnUrlHelper.NormalizeAfterLogin(returnUrl));
    }
}
