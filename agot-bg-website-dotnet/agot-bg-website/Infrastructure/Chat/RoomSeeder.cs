using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace agot_bg_website.Infrastructure.Chat;

/// <summary>
/// Ensures the two well-known public chat rooms exist and caches their ids, mirroring Django's
/// <c>get_public_room_id</c>/<c>get_issues_room_id</c> helpers (agotboardgame_main/views.py) and
/// the <c>0004_create_public_room</c> data migration. Idempotent — safe to run on every startup.
///
/// Auto-creation only ever happens in the Development environment (a genuinely empty local dev
/// database). Staging/Production always get these two rooms from the legacy data import
/// (Snr.Migration) instead — creating a fresh stub there would just leave a dangling extra room
/// behind once the import later inserts the real one under its preserved legacy id (nothing would
/// ever merge the two). If a Staging/Production `website` happens to start before the import has
/// ever run against a freshly-reset database (see MIGRATION_PLAN.md §17.4), the room legitimately
/// doesn't exist yet — <see cref="PublicRoomId"/>/<see cref="IssuesRoomId"/> stay unset and throw
/// until `website` is restarted after the import runs (already the documented required step).
/// See MIGRATION_PLAN.md §7.
/// </summary>
public static class RoomSeeder
{
    public const string PublicRoomName = "public";
    public const string IssuesRoomName = "issues";

    private static Guid? _publicRoomId;
    private static Guid? _issuesRoomId;

    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment environment)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        _publicRoomId = await GetOrCreateAsync(
            db,
            PublicRoomName,
            environment,
            maxRetrieveCount: 50
        );
        _issuesRoomId = await GetOrCreateAsync(
            db,
            IssuesRoomName,
            environment,
            maxRetrieveCount: 50
        );
    }

    private static async Task<Guid?> GetOrCreateAsync(
        ApplicationDbContext db,
        string name,
        IHostEnvironment environment,
        int maxRetrieveCount
    )
    {
        // OrderBy(CreatedAt) makes this deterministic if duplicate rows ever exist for the same
        // name (e.g. this seeder ran once before a legacy-data import created another room with
        // the same name but a different id) — always prefer the oldest/original row instead of
        // whichever Postgres happens to return first.
        var room = await db
            .Rooms.Where(r => r.Name == name)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        if (room is null)
        {
            if (!environment.IsDevelopment())
            {
                // Not an error by itself (see the "freshly-reset database" case above) — just
                // leave it unset. PublicRoomId/IssuesRoomId throw a clear message if anything
                // actually tries to use it before the room exists.
                return null;
            }

            room = new Room
            {
                Id = Guid.NewGuid(),
                Name = name,
                Public = true,
                MaxRetrieveCount = maxRetrieveCount,
            };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
        }

        return room.Id;
    }

    /// <summary>
    /// Cached after <see cref="SeedAsync"/> runs at startup. Null only in Staging/Production when
    /// the legacy data import hasn't populated this room yet (restart `website` after running it).
    /// </summary>
    public static Guid PublicRoomId =>
        _publicRoomId
        ?? throw new InvalidOperationException(
            $"No '{PublicRoomName}' room exists yet — run Snr.Migration's import, then restart "
                + $"{nameof(RoomSeeder)} (e.g. restart the website), or start it in Development."
        );

    /// <summary>
    /// Cached after <see cref="SeedAsync"/> runs at startup. Null only in Staging/Production when
    /// the legacy data import hasn't populated this room yet (restart `website` after running it).
    /// </summary>
    public static Guid IssuesRoomId =>
        _issuesRoomId
        ?? throw new InvalidOperationException(
            $"No '{IssuesRoomName}' room exists yet — run Snr.Migration's import, then restart "
                + $"{nameof(RoomSeeder)} (e.g. restart the website), or start it in Development."
        );
}
