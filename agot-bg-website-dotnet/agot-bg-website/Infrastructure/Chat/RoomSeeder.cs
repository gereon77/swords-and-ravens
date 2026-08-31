using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Infrastructure.Chat;

/// <summary>
/// Ensures the two well-known public chat rooms exist and caches their ids, mirroring Django's
/// <c>get_public_room_id</c>/<c>get_issues_room_id</c> helpers (agotboardgame_main/views.py) and
/// the <c>0004_create_public_room</c> data migration. Idempotent — safe to run on every startup.
/// See MIGRATION_PLAN.md §7.
/// </summary>
public static class RoomSeeder
{
    public const string PublicRoomName = "public";
    public const string IssuesRoomName = "issues";

    private static Guid? _publicRoomId;
    private static Guid? _issuesRoomId;

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        _publicRoomId = await GetOrCreateAsync(db, PublicRoomName, maxRetrieveCount: 50);
        _issuesRoomId = await GetOrCreateAsync(db, IssuesRoomName, maxRetrieveCount: 50);
    }

    private static async Task<Guid> GetOrCreateAsync(ApplicationDbContext db, string name, int maxRetrieveCount)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Name == name);
        if (room is null)
        {
            room = new Room
            {
                Id = Guid.NewGuid(),
                Name = name,
                Public = true,
                MaxRetrieveCount = maxRetrieveCount
            };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
        }

        return room.Id;
    }

    /// <summary>Cached after <see cref="SeedAsync"/> runs at startup — never null once the app is running.</summary>
    public static Guid PublicRoomId => _publicRoomId ?? throw new InvalidOperationException($"{nameof(RoomSeeder)}.{nameof(SeedAsync)} has not run yet.");

    /// <summary>Cached after <see cref="SeedAsync"/> runs at startup — never null once the app is running.</summary>
    public static Guid IssuesRoomId => _issuesRoomId ?? throw new InvalidOperationException($"{nameof(RoomSeeder)}.{nameof(SeedAsync)} has not run yet.");
}
