using System.Security.Claims;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Infrastructure.Stats;
using agot_bg_website.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Pages;

/// <summary>
/// Covers the public users directory/ranking page (Pages/Users.cshtml.cs): sorting by each cached
/// stats column in both directions, and that a user whose stats have never been cached gets
/// enqueued for background recalculation instead of the page computing anything inline.
/// </summary>
public class UsersModelTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserStatsRecalculationQueue _userStatsQueue;

    public UsersModelTests()
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
        services.AddSingleton<UserStatsRecalculationQueue>();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        _authorizationService = _provider.GetRequiredService<IAuthorizationService>();
        _userStatsQueue = _provider.GetRequiredService<UserStatsRecalculationQueue>();
    }

    private UsersModel CreatePageModel(ClaimsPrincipal? viewer = null) =>
        new(_userManager, _authorizationService, _db, _userStatsQueue)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = viewer ?? new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };

    /// <summary>A signed-in principal holding the <see cref="GamePermissions.ManageUserStatus"/>
    /// claim directly (the same "one-off claim on a single user" grant path covered by
    /// GamePermissionsTests), so tests can exercise the status-filter/moderation UI without
    /// needing a full role assignment round-trip.</summary>
    private static ClaimsPrincipal ManageUserStatusViewer() =>
        new(
            new ClaimsIdentity(
                [new Claim(GamePermissions.ClaimType, GamePermissions.ManageUserStatus)],
                authenticationType: "Test"
            )
        );

    private async Task<ApplicationUser> CreateUserAsync(
        string userName,
        int? finished,
        int? won,
        int? removed,
        double? winRate,
        DateTimeOffset? lastActivity = null
    )
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            CachedFinishedGamesCount = finished,
            CachedWonGamesCount = won,
            CachedRemovedFromGameCount = removed,
            CachedWinRate = winRate,
            StatsCachedAt = finished is null ? null : DateTimeOffset.UtcNow,
            LastActivity = lastActivity ?? DateTimeOffset.UtcNow,
        };
        await _userManager.CreateAsync(user);
        return user;
    }

    [Fact]
    public async Task SortByWonGames_Descending_OrdersHighestFirst()
    {
        await CreateUserAsync("low", finished: 5, won: 1, removed: 0, winRate: 0.2);
        await CreateUserAsync("high", finished: 10, won: 8, removed: 0, winRate: 0.8);
        await CreateUserAsync("mid", finished: 6, won: 3, removed: 0, winRate: 0.5);

        var model = CreatePageModel();
        model.SortBy = "won";
        model.SortDir = "desc";
        await model.OnGetAsync();

        Assert.Equal(["high", "mid", "low"], model.Users.Select(u => u.UserName));
    }

    [Fact]
    public async Task SortByLastActivity_Descending_OrdersMostRecentFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await CreateUserAsync(
            "stale",
            finished: 1,
            won: 1,
            removed: 0,
            winRate: 1.0,
            lastActivity: now.AddDays(-30)
        );
        await CreateUserAsync(
            "active",
            finished: 1,
            won: 0,
            removed: 0,
            winRate: 0.0,
            lastActivity: now
        );

        var model = CreatePageModel();
        model.SortBy = "activity";
        model.SortDir = "desc";
        await model.OnGetAsync();

        Assert.Equal(["active", "stale"], model.Users.Select(u => u.UserName));
    }

    [Fact]
    public async Task SortByWinRate_Ascending_OrdersLowestFirst()
    {
        await CreateUserAsync("low", finished: 5, won: 1, removed: 0, winRate: 0.2);
        await CreateUserAsync("high", finished: 10, won: 8, removed: 0, winRate: 0.8);

        var model = CreatePageModel();
        model.SortBy = "winrate";
        model.SortDir = "asc";
        await model.OnGetAsync();

        Assert.Equal(["low", "high"], model.Users.Select(u => u.UserName));
    }

    /// <summary>
    /// Regression test for the reported bug: a null win rate (no finished games yet, or stats
    /// never recalculated) must never outrank an actual ranked player - on Postgres, the naive
    /// `OrderByDescending(u => u.CachedWinRate)` puts NULLs FIRST for a descending sort (Postgres's
    /// default null ordering), which buried real high-win-rate players many pages deep behind a
    /// wall of "n/a" accounts, making sorting look like it only reordered the current page. Nulls
    /// must land last regardless of direction.
    /// </summary>
    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task SortByWinRate_NullWinRateAlwaysSortsLast(string sortDir)
    {
        await CreateUserAsync("unranked", finished: 0, won: 0, removed: 0, winRate: null);
        await CreateUserAsync("ranked", finished: 5, won: 4, removed: 0, winRate: 0.8);

        var model = CreatePageModel();
        model.SortBy = "winrate";
        model.SortDir = sortDir;
        await model.OnGetAsync();

        Assert.Equal(["ranked", "unranked"], model.Users.Select(u => u.UserName));
    }

    [Fact]
    public async Task InvalidSortBy_FallsBackToUsernameAscending()
    {
        await CreateUserAsync("zeta", finished: 1, won: 1, removed: 0, winRate: 1.0);
        await CreateUserAsync("alpha", finished: 1, won: 0, removed: 0, winRate: 0.0);

        var model = CreatePageModel();
        model.SortBy = "not-a-real-column";
        await model.OnGetAsync();

        Assert.Equal("username", model.SortBy);
        Assert.Equal(["alpha", "zeta"], model.Users.Select(u => u.UserName));
    }

    [Fact]
    public void NextSortDir_TogglesActiveColumnAndDefaultsOthers()
    {
        var model = CreatePageModel();
        model.SortBy = "won";
        model.SortDir = "desc";

        // Clicking the already-active column toggles direction...
        Assert.Equal("asc", model.NextSortDir("won"));
        // ...while clicking a different stats column starts at that column's own default (desc,
        // i.e. best-first for a ranking column) regardless of the currently active column/direction.
        Assert.Equal("desc", model.NextSortDir("winrate"));
        // Username's default first-click direction is ascending, matching the previous
        // (pre-sorting) implicit behaviour.
        Assert.Equal("asc", model.NextSortDir("username"));
    }

    [Fact]
    public async Task UserWithUncachedStats_IsEnqueuedForBackgroundRecalculation()
    {
        var user = await CreateUserAsync(
            "uncached",
            finished: null,
            won: null,
            removed: null,
            winRate: null
        );

        var model = CreatePageModel();
        await model.OnGetAsync();

        await using var enumerator = _userStatsQueue
            .ReadAllAsync(CancellationToken.None)
            .GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(user.Id, enumerator.Current);
    }

    [Fact]
    public async Task StatusFilter_WithManageUserStatusPermission_FiltersToRoleMembers()
    {
        var banned = await CreateUserAsync("banned", finished: 1, won: 1, removed: 0, winRate: 1.0);
        await _roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.Banned));
        await _userManager.AddToRoleAsync(banned, RoleNames.Banned);
        await CreateUserAsync("regular", finished: 1, won: 0, removed: 0, winRate: 0.0);

        var model = CreatePageModel(ManageUserStatusViewer());
        model.StatusFilter = "banned";
        await model.OnGetAsync();

        Assert.Equal(["banned"], model.Users.Select(u => u.UserName));
    }

    /// <summary>A viewer without <see cref="GamePermissions.ManageUserStatus"/> must never be
    /// able to use StatusFilter to enumerate who's on probation/tongueless/banned - the filter is
    /// silently dropped for them rather than honored, so the query just returns everyone.</summary>
    [Fact]
    public async Task StatusFilter_WithoutManageUserStatusPermission_IsIgnored()
    {
        var banned = await CreateUserAsync("banned", finished: 1, won: 1, removed: 0, winRate: 1.0);
        await _roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.Banned));
        await _userManager.AddToRoleAsync(banned, RoleNames.Banned);
        await CreateUserAsync("regular", finished: 1, won: 0, removed: 0, winRate: 0.0);

        var model = CreatePageModel();
        model.StatusFilter = "banned";
        await model.OnGetAsync();

        Assert.Null(model.StatusFilter);
        Assert.Equal(["banned", "regular"], model.Users.Select(u => u.UserName));
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}
