using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace agot_bg_website.Tests.Services;

/// <summary>
/// Exercises the "Took the Black" soft-delete pipeline against a real (InMemory) Identity
/// UserManager + ApplicationDbContext - see MIGRATION_PLAN.md §13.
/// </summary>
public class AccountDeletionServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AccountDeletionService _sut;

    public AccountDeletionServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
        );
        services.AddLogging();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                // Mirror Program.cs's Identity configuration so this test exercises the same
                // validators production actually runs under - RequireUniqueEmail rejects a
                // null/empty Email regardless of the column itself being nullable, which is
                // exactly what broke soft-deletion (see AccountDeletionService).
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        _sut = new AccountDeletionService(
            _userManager,
            NullLogger<AccountDeletionService>.Instance
        );
    }

    private async Task<ApplicationUser> CreateUserAsync()
    {
        if (!await _db.Roles.AnyAsync(r => r.Name == RoleNames.Member))
        {
            _db.Roles.Add(
                new IdentityRole<Guid>(RoleNames.Member)
                {
                    NormalizedName = RoleNames.Member.ToUpperInvariant(),
                }
            );
            await _db.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = "robb_stark",
            Email = "robb@example.com",
            NormalizedEmail = "ROBB@EXAMPLE.COM",
            EmailConfirmed = true,
            ProfileText = "King in the North",
            LastWonTournament = "Riverrun Cup",
        };
        var createResult = await _userManager.CreateAsync(user, "P@ssw0rd123!");
        Assert.True(createResult.Succeeded);
        await _userManager.AddToRoleAsync(user, RoleNames.Member);
        return user;
    }

    [Fact]
    public async Task DeleteAccount_StripsPiiAndFlagsAsDeleted()
    {
        var user = await CreateUserAsync();
        var originalGameToken = user.GameToken;

        var result = await _sut.DeleteAccountAsync(user);

        Assert.True(result.Succeeded);
        Assert.True(user.IsDeleted);
        Assert.NotNull(user.DeletedAt);
        Assert.Equal(user.Id.ToString(), user.UserName);
        Assert.Equal($"{user.Id:N}@deleted.invalid", user.Email);
        Assert.Equal(user.Email!.ToUpperInvariant(), user.NormalizedEmail);
        Assert.Null(user.PasswordHash);
        Assert.Null(user.ProfileText);
        Assert.Null(user.LastWonTournament);
        Assert.False(user.EmailConfirmed);
        Assert.False(user.EmailNotificationActive);
        Assert.NotEqual(originalGameToken, user.GameToken);
        Assert.Equal("Took the Black", user.DisplayName);
        Assert.Empty(await _userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task DeleteAccount_LocksAccountOutAndRotatesSecurityStamp()
    {
        var user = await CreateUserAsync();
        var originalStamp = user.SecurityStamp;

        await _sut.DeleteAccountAsync(user);

        Assert.True(user.LockoutEnabled);
        Assert.Equal(DateTimeOffset.MaxValue, user.LockoutEnd);
        Assert.NotEqual(originalStamp, user.SecurityStamp);
    }

    [Fact]
    public async Task DeleteAccount_IsIdempotent()
    {
        var user = await CreateUserAsync();
        await _sut.DeleteAccountAsync(user);

        var result = await _sut.DeleteAccountAsync(user);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteAccount_AllowsAnotherUserToReuseTheEmailAndUsername()
    {
        var user = await CreateUserAsync();
        await _sut.DeleteAccountAsync(user);

        // Both the username and email free up for reuse - the placeholder UserName we substitute
        // in is namespaced under a "deleted-user-" prefix precisely so this doesn't collide.
        var newUser = new ApplicationUser
        {
            UserName = "robb_stark",
            Email = "robb@example.com",
            NormalizedEmail = "ROBB@EXAMPLE.COM",
        };
        var createResult = await _userManager.CreateAsync(newUser, "P@ssw0rd123!");

        Assert.True(createResult.Succeeded);
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}
