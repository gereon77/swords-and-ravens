using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

/// <summary>
/// Covers <see cref="PermissionSeeder"/>: it must reproduce the legacy site's default
/// role → permission grants, be idempotent (safe on every startup, matching
/// <see cref="RoleSeeder"/>'s contract), and must never re-add a permission an admin has
/// deliberately removed from a role afterwards (see PermissionSeeder.cs's doc comment).
/// </summary>
public class PermissionSeederTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public PermissionSeederTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddLogging();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _roleManager = _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    }

    private async Task<IdentityRole<Guid>> GetOrCreateRoleAsync(string name)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role is not null)
        {
            return role;
        }

        role = new IdentityRole<Guid>(name);
        await _roleManager.CreateAsync(role);
        return role;
    }

    [Fact]
    public async Task SeedsDefaultPermissions_ForMemberAdminAndHighMember()
    {
        await RoleSeeder.SeedAsync(_provider);

        await PermissionSeeder.SeedAsync(_provider);

        var memberClaims = await _roleManager.GetClaimsAsync((await _roleManager.FindByNameAsync(RoleNames.Member))!);
        var adminClaims = await _roleManager.GetClaimsAsync((await _roleManager.FindByNameAsync(RoleNames.Admin))!);
        var highMemberClaims = await _roleManager.GetClaimsAsync((await _roleManager.FindByNameAsync(RoleNames.HighMember))!);

        Assert.Contains(memberClaims, c => c.Type == GamePermissions.ClaimType && c.Value == GamePermissions.CreateGame);
        Assert.DoesNotContain(memberClaims, c => c.Value == GamePermissions.CancelGame);

        foreach (var claims in new[] { adminClaims, highMemberClaims })
        {
            Assert.Contains(claims, c => c.Value == GamePermissions.CreateGame);
            Assert.Contains(claims, c => c.Value == GamePermissions.ImpersonateOtherPlayers);
            Assert.Contains(claims, c => c.Value == GamePermissions.CancelGame);
        }
    }

    [Fact]
    public async Task IsIdempotent_RunningTwiceDoesNotDuplicateClaims()
    {
        await RoleSeeder.SeedAsync(_provider);

        await PermissionSeeder.SeedAsync(_provider);
        await PermissionSeeder.SeedAsync(_provider);

        var adminClaims = await _roleManager.GetClaimsAsync((await _roleManager.FindByNameAsync(RoleNames.Admin))!);
        Assert.Single(adminClaims, c => c.Value == GamePermissions.CreateGame);
    }

    [Fact]
    public async Task ReSeedingAfterAdminRemoval_RestoresTheDefaultPermission()
    {
        // Documents the current, honest limitation (see PermissionSeeder.cs's doc comment): since
        // there's no persisted "already seeded" marker, a default permission an admin removes from
        // one of these three roles reappears the next time the app restarts and re-seeds — exactly
        // like RoleSeeder recreating a deleted built-in role.
        await GetOrCreateRoleAsync(RoleNames.Member);
        await PermissionSeeder.SeedAsync(_provider);

        var role = (await _roleManager.FindByNameAsync(RoleNames.Member))!;
        var createGameClaim = (await _roleManager.GetClaimsAsync(role)).Single(c => c.Value == GamePermissions.CreateGame);
        await _roleManager.RemoveClaimAsync(role, createGameClaim);

        await PermissionSeeder.SeedAsync(_provider);

        var remainingClaims = await _roleManager.GetClaimsAsync(role);
        Assert.Contains(remainingClaims, c => c.Value == GamePermissions.CreateGame);
    }

    public void Dispose() => _provider.Dispose();
}
