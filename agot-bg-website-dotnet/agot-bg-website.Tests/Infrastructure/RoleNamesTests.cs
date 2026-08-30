using agot_bg_website.Infrastructure.Auth;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

public class RoleNamesTests
{
    [Fact]
    public void All_ContainsBannedAndOnProbation_UsedByPlayApiChecks()
    {
        Assert.Contains(RoleNames.Banned, RoleNames.All);
        Assert.Contains(RoleNames.OnProbation, RoleNames.All);
    }

    [Fact]
    public void CanPlayAsAnotherPlayer_OnlyGrantedToAdminAndHighMember()
    {
        Assert.Equal([RoleNames.Admin, RoleNames.HighMember], RoleNames.CanPlayAsAnotherPlayer);
        Assert.DoesNotContain(RoleNames.Member, RoleNames.CanPlayAsAnotherPlayer);
        Assert.DoesNotContain(RoleNames.Banned, RoleNames.CanPlayAsAnotherPlayer);
    }
}
