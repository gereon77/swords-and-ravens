using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

/// <summary>
/// Reproduces the exact production incident this validator fixes: several legacy-imported rows
/// share the same blank "" NormalizedEmail, and the built-in UserValidator&lt;TUser&gt;'s
/// FindByEmailAsync-based uniqueness check throws InvalidOperationException ("Sequence contains
/// more than one element") instead of returning a validation error whenever it's asked to validate
/// one of those rows - crashing Ban/Unban, Edit roles, and account deletion (all of which revalidate
/// the user via UserManager.UpdateAsync internally). See SafeUserValidator's own doc comment.
/// </summary>
public class SafeUserValidatorTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public SafeUserValidatorTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
        );
        services.AddLogging();
        // Mirrors Program.cs: SafeUserValidator must be registered *before* AddIdentity() so its
        // TryAddScoped<IUserValidator<TUser>, UserValidator<TUser>>() call is a no-op.
        services.AddScoped<IUserValidator<ApplicationUser>, SafeUserValidator>();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();

        var roleManager = _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        roleManager.CreateAsync(new IdentityRole<Guid>("Banned")).GetAwaiter().GetResult();
    }

    private async Task<ApplicationUser> CreateUserWithBlankEmailAsync(string userName)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = "",
            NormalizedEmail = "",
            EmailConfirmed = false,
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        // Bypass UserManager.CreateAsync (which would itself run - and reject - email validation)
        // by inserting directly, exactly like the legacy Django import did.
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task RemoveFromRole_OnUserWithBlankEmail_DoesNotThrowWhenMultipleShareIt()
    {
        // Three legacy rows sharing the same blank email, matching production's actual shape.
        await CreateUserWithBlankEmailAsync("stannis");
        await CreateUserWithBlankEmailAsync("davos");
        var target = await CreateUserWithBlankEmailAsync("melisandre");

        await _userManager.AddToRoleAsync(target, "Banned");

        // This is the exact call chain that crashed in production: RemoveFromRoleAsync ->
        // RemoveFromRolesCoreAsync -> UpdateUserAsync -> ValidateUserAsync -> ValidateEmail ->
        // FindByEmailAsync("") -> SingleOrDefaultAsync throws because 3 rows match "".
        var exception = await Record.ExceptionAsync(() =>
            _userManager.RemoveFromRoleAsync(target, "Banned")
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task AddToRole_OnUserWithBlankEmail_DoesNotThrowWhenMultipleShareIt()
    {
        await CreateUserWithBlankEmailAsync("robert");
        var target = await CreateUserWithBlankEmailAsync("cersei");

        var exception = await Record.ExceptionAsync(() =>
            _userManager.AddToRoleAsync(target, "Banned")
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task ValidateAsync_RealDuplicateEmail_FailsGracefullyInsteadOfThrowing()
    {
        var first = new ApplicationUser
        {
            UserName = "jaime",
            NormalizedUserName = "JAIME",
            Email = "lannister@example.com",
            NormalizedEmail = "LANNISTER@EXAMPLE.COM",
        };
        var second = new ApplicationUser
        {
            UserName = "tyrion",
            NormalizedUserName = "TYRION",
            Email = "lannister@example.com",
            NormalizedEmail = "LANNISTER@EXAMPLE.COM",
        };
        _db.Users.AddRange(first, second);
        await _db.SaveChangesAsync();

        var validator = _provider.GetRequiredService<IUserValidator<ApplicationUser>>();

        var exception = await Record.ExceptionAsync(() =>
            validator.ValidateAsync(_userManager, second)
        );
        Assert.Null(exception);

        var result = await validator.ValidateAsync(_userManager, second);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "DuplicateEmail");
    }

    [Fact]
    public async Task ValidateAsync_UniqueEmail_Succeeds()
    {
        var user = new ApplicationUser
        {
            UserName = "brienne",
            NormalizedUserName = "BRIENNE",
            Email = "brienne@example.com",
            NormalizedEmail = "BRIENNE@EXAMPLE.COM",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var validator = _provider.GetRequiredService<IUserValidator<ApplicationUser>>();
        var result = await validator.ValidateAsync(_userManager, user);

        Assert.True(result.Succeeded);
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}
