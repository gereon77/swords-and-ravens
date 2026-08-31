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

    [Fact]
    public void CanCreateGame_ReturnsTrueForMemberAdminHighMember()
    {
        var memberPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "alice"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleNames.Member)
        ], "TestAuth"));

        var adminPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "bob"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleNames.Admin)
        ], "TestAuth"));

        Assert.True(RoleNames.CanCreateGame(memberPrincipal));
        Assert.True(RoleNames.CanCreateGame(adminPrincipal));
    }

    [Fact]
    public void CanCreateGame_ReturnsFalseForBannedOrProbationOrAnonymous()
    {
        var anonPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        var bannedPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "bannedUser"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleNames.Member),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleNames.Banned)
        ], "TestAuth"));

        var probationPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "probationUser"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleNames.Member),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleNames.OnProbation)
        ], "TestAuth"));

        Assert.False(RoleNames.CanCreateGame(anonPrincipal));
        Assert.False(RoleNames.CanCreateGame(bannedPrincipal));
        Assert.False(RoleNames.CanCreateGame(probationPrincipal));
    }
}
