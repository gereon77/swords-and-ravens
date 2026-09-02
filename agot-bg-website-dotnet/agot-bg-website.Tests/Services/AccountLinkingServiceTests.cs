using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Services;

/// <summary>
/// Exercises the account-linking/"claiming" pipeline against a real (InMemory) Identity
/// UserManager + ApplicationDbContext, since it depends on UserManager's query/update behavior —
/// see MIGRATION_PLAN.md §5.3.
/// </summary>
public class AccountLinkingServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AccountLinkingService _sut;

    public AccountLinkingServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddLogging();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        _sut = new AccountLinkingService(_userManager);
    }

    [Fact]
    public async Task NoExistingUser_ReturnsNoMatch()
    {
        var result = await _sut.TryLinkByEmailAsync("nobody@example.com");

        Assert.Equal(AccountLinkOutcome.NoMatch, result.Outcome);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task ImportedUnclaimedUser_GetsLinkedAndClaimed()
    {
        var imported = new ApplicationUser
        {
            UserName = "legacyuser",
            Email = "legacy@example.com",
            NormalizedEmail = "LEGACY@EXAMPLE.COM",
            ImportedFromLegacy = true,
            Claimed = false
        };
        await _userManager.CreateAsync(imported);

        var result = await _sut.TryLinkByEmailAsync("LEGACY@EXAMPLE.COM");

        Assert.Equal(AccountLinkOutcome.Linked, result.Outcome);
        Assert.NotNull(result.User);
        Assert.True(result.User!.Claimed);
        Assert.True(result.User.EmailConfirmed);
    }

    [Fact]
    public async Task AlreadyClaimedUser_ReturnsConflict_DoesNotSilentlyMerge()
    {
        var claimed = new ApplicationUser
        {
            UserName = "activeuser",
            Email = "active@example.com",
            NormalizedEmail = "ACTIVE@EXAMPLE.COM",
            ImportedFromLegacy = true,
            Claimed = true
        };
        await _userManager.CreateAsync(claimed);

        var result = await _sut.TryLinkByEmailAsync("ACTIVE@EXAMPLE.COM");

        Assert.Equal(AccountLinkOutcome.ConflictAlreadyClaimed, result.Outcome);
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}
