using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

public class ReservedUsernamesTests
{
    [Theory]
    [InlineData("Took the Black")]
    [InlineData("took the black")]
    [InlineData("TOOK THE BLACK")]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("Administrator")]
    [InlineData("High Member")]
    [InlineData("high member")]
    [InlineData("Moderator")]
    [InlineData("Banned")]
    public void IsReserved_MatchesKnownReservedNamesCaseInsensitively(string userName)
    {
        Assert.True(ReservedUsernames.IsReserved(userName));
    }

    [Theory]
    [InlineData("John Admin")]
    [InlineData("Admin John")]
    [InlineData("our new moderator")]
    public void IsReserved_MatchesASingleWordReservedNameAnywhereInALongerUsername(string userName)
    {
        Assert.True(ReservedUsernames.IsReserved(userName));
    }

    [Theory]
    [InlineData("  Admin ")]
    [InlineData("Admin  ")]
    [InlineData("  Took   the    Black  ")]
    public void IsReserved_CollapsesAndTrimsWhitespaceBeforeMatching(string userName)
    {
        Assert.True(ReservedUsernames.IsReserved(userName));
    }

    [Theory]
    [InlineData("gereon77")]
    [InlineData("AdminFan")]
    [InlineData("High Tower")]
    [InlineData("")]
    [InlineData(null)]
    public void IsReserved_DoesNotFlagUnrelatedOrEmptyNames(string? userName)
    {
        Assert.False(ReservedUsernames.IsReserved(userName));
    }

    [Fact]
    public void ReservedList_MatchesTheDeletedAccountDisplayName()
    {
        Assert.True(ReservedUsernames.IsReserved(ApplicationUser.DeletedAccountDisplayName));
    }
}
