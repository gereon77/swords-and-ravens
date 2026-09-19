using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Services.GameListing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Services;

/// <summary>
/// Pins <see cref="GameListQueryService.GetLastFinishedGameAsync"/>'s ordering rule: it must pick
/// the Finished game with the most recent UpdatedAt (bumped by every save, including the one that
/// transitions State to Finished), not the one with the most recent CreatedAt. A game created long
/// ago can still finish more recently than a game created (and finished) later, and the widget
/// must reflect the latter as "last finished" the moment it happens.
/// </summary>
public class GameListQueryServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GameListQueryService _sut;

    public GameListQueryServiceTests()
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

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        _sut = new GameListQueryService(_db);
    }

    private static JsonDocument ViewOfGame(int maxPlayerCount) =>
        JsonDocument.Parse($$"""{"maxPlayerCount": {{maxPlayerCount}}}""");

    [Fact]
    public async Task GetLastFinishedGameAsync_PicksMostRecentlyUpdated_NotMostRecentlyCreated()
    {
        var owner = new ApplicationUser { UserName = "owner", Email = "owner@example.com" };
        await _userManager.CreateAsync(owner);

        // Created first but finished (and thus last saved) most recently.
        var recentlyFinished = new Game
        {
            Id = Guid.NewGuid(),
            Name = "Old game, finished recently",
            OwnerUserId = owner.Id,
            State = GameState.Finished,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow,
            ViewOfGame = ViewOfGame(6),
        };

        // Created after the game above, but finished (and last saved) long before it.
        var createdLaterButFinishedEarlier = new Game
        {
            Id = Guid.NewGuid(),
            Name = "New game, finished long ago",
            OwnerUserId = owner.Id,
            State = GameState.Finished,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-9),
            ViewOfGame = ViewOfGame(6),
        };

        _db.Games.AddRange(recentlyFinished, createdLaterButFinishedEarlier);
        await _db.SaveChangesAsync();

        var result = await _sut.GetLastFinishedGameAsync();

        Assert.NotNull(result);
        Assert.Equal(recentlyFinished.Id, result!.Id);
    }

    [Fact]
    public async Task GetLastFinishedGameAsync_ReturnsNull_WhenNoGameHasFinished()
    {
        var owner = new ApplicationUser { UserName = "owner2", Email = "owner2@example.com" };
        await _userManager.CreateAsync(owner);

        _db.Games.Add(
            new Game
            {
                Id = Guid.NewGuid(),
                Name = "Still ongoing",
                OwnerUserId = owner.Id,
                State = GameState.Ongoing,
                ViewOfGame = ViewOfGame(6),
            }
        );
        await _db.SaveChangesAsync();

        var result = await _sut.GetLastFinishedGameAsync();

        Assert.Null(result);
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }
}
