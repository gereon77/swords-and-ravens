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
}
