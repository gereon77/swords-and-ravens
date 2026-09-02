using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

/// <summary>
/// Covers <see cref="GamePermissions"/>'s policies end-to-end through the real
/// <see cref="IAuthorizationService"/>/<see cref="IUserClaimsPrincipalFactory{TUser}"/> pipeline
/// (not just a static helper method), including the two ways a permission can be granted — via a
/// role's claims (<see cref="PermissionSeeder"/>'s default grants, or a future Admin-area "edit
/// role permissions" page) and via a one-off claim on a single user (a future Admin-area "grant
/// permission to this user" page) — since both are meant to work without any further code changes
/// (see GamePermissions.cs's doc comment).
/// </summary>
public class GamePermissionsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserClaimsPrincipalFactory<ApplicationUser> _principalFactory;
    private readonly IAuthorizationService _authorizationService;

    public GamePermissionsTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
        );
        services.AddLogging();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddAuthorizationBuilder().AddGamePermissionPolicies();

        _provider = services.BuildServiceProvider();
        _roleManager = _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        _principalFactory = _provider.GetRequiredService<
            IUserClaimsPrincipalFactory<ApplicationUser>
        >();
        _authorizationService = _provider.GetRequiredService<IAuthorizationService>();
    }

    private async Task<ApplicationUser> CreateUserAsync(string name)
    {
        var user = new ApplicationUser { UserName = name, Email = $"{name}@example.com" };
        var result = await _userManager.CreateAsync(user);
        Assert.True(result.Succeeded);
        return user;
    }

    [Fact]
    public async Task RoleGrantedPermission_FlowsToUserPrincipal_ViaRoleClaims()
    {
        // Mirrors PermissionSeeder/RoleManager.AddClaimAsync — a permission granted to a role
        // (the "assign permissions to a role" admin feature).
        await _roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.HighMember));
        await _roleManager.AddClaimAsync(
            (await _roleManager.FindByNameAsync(RoleNames.HighMember))!,
            new System.Security.Claims.Claim(GamePermissions.ClaimType, GamePermissions.CancelGame)
        );

        var user = await CreateUserAsync("high-member-user");
        await _userManager.AddToRoleAsync(user, RoleNames.HighMember);

        var principal = await _principalFactory.CreateAsync(user);

        var result = await _authorizationService.AuthorizeAsync(
            principal,
            GamePermissions.CancelGame
        );

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DirectlyGrantedPermission_FlowsToUserPrincipal_ViaUserClaims()
    {
        // Mirrors UserManager.AddClaimAsync — a one-off permission granted straight to a single
        // user with no role involved (the "assign permissions to a user" admin feature).
        var user = await CreateUserAsync("special-case-user");
        await _userManager.AddClaimAsync(
            user,
            new System.Security.Claims.Claim(
                GamePermissions.ClaimType,
                GamePermissions.ImpersonateOtherPlayers
            )
        );

        var principal = await _principalFactory.CreateAsync(user);

        var result = await _authorizationService.AuthorizeAsync(
            principal,
            GamePermissions.ImpersonateOtherPlayers
        );

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UserWithoutPermissionClaim_IsDenied()
    {
        var user = await CreateUserAsync("plain-user");
        var principal = await _principalFactory.CreateAsync(user);

        var cancelResult = await _authorizationService.AuthorizeAsync(
            principal,
            GamePermissions.CancelGame
        );
        var impersonateResult = await _authorizationService.AuthorizeAsync(
            principal,
            GamePermissions.ImpersonateOtherPlayers
        );

        Assert.False(cancelResult.Succeeded);
        Assert.False(impersonateResult.Succeeded);
    }

    [Fact]
    public async Task CreateGame_RequiresPermissionClaim_AndDeniesBannedOrOnProbationEvenWithClaim()
    {
        await _roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.Banned));

        var withoutClaim = await CreateUserAsync("no-permission-user");
        var withoutClaimPrincipal = await _principalFactory.CreateAsync(withoutClaim);
        Assert.False(
            (
                await _authorizationService.AuthorizeAsync(
                    withoutClaimPrincipal,
                    GamePermissions.CreateGame
                )
            ).Succeeded
        );

        var granted = await CreateUserAsync("granted-user");
        await _userManager.AddClaimAsync(
            granted,
            new System.Security.Claims.Claim(GamePermissions.ClaimType, GamePermissions.CreateGame)
        );
        var grantedPrincipal = await _principalFactory.CreateAsync(granted);
        Assert.True(
            (
                await _authorizationService.AuthorizeAsync(
                    grantedPrincipal,
                    GamePermissions.CreateGame
                )
            ).Succeeded
        );

        var bannedButGranted = await CreateUserAsync("banned-user");
        await _userManager.AddClaimAsync(
            bannedButGranted,
            new System.Security.Claims.Claim(GamePermissions.ClaimType, GamePermissions.CreateGame)
        );
        await _userManager.AddToRoleAsync(bannedButGranted, RoleNames.Banned);
        var bannedPrincipal = await _principalFactory.CreateAsync(bannedButGranted);
        Assert.False(
            (
                await _authorizationService.AuthorizeAsync(
                    bannedPrincipal,
                    GamePermissions.CreateGame
                )
            ).Succeeded
        );
    }

    public void Dispose() => _provider.Dispose();
}
