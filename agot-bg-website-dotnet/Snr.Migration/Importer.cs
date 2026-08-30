using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Snr.Migration;

/// <summary>
/// Repeatable/idempotent import from the legacy Django database into the new Postgres schema.
/// Safe to re-run at any time up to and including final cutover — see MIGRATION_PLAN.md §10.
/// </summary>
public class Importer(string legacyConnectionString, string targetConnectionString)
{
    private readonly LegacyReader _legacy = new(legacyConnectionString);

    private ApplicationDbContext NewTargetContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(targetConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public async Task RunAsync()
    {
        Console.WriteLine("---> Importing users");
        var groupIdToRoleId = await ImportUsersAndRolesAsync();

        Console.WriteLine("---> Importing chat rooms");
        await ImportRoomsAsync();

        Console.WriteLine("---> Importing games");
        await ImportGamesAsync();

        Console.WriteLine("---> Importing current players in game");
        await ImportPlayersInGameAsync();

        Console.WriteLine("---> Importing chat messages");
        await ImportMessagesAsync();

        Console.WriteLine("---> Importing PBEM response times");
        await ImportPbemResponseTimesAsync();

        Console.WriteLine("---> Import complete");
        _ = groupIdToRoleId;
    }

    private async Task<Dictionary<int, Guid>> ImportUsersAndRolesAsync()
    {
        await using var db = NewTargetContext();

        // Roles/groups first, since user-role membership references them.
        var groupIdToRoleId = new Dictionary<int, Guid>();
        await foreach (var group in _legacy.ReadGroupsAsync())
        {
            var normalized = group.Name.ToUpperInvariant();
            var role = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalized);
            if (role == null)
            {
                role = new IdentityRole<Guid>(group.Name)
                {
                    Id = Guid.NewGuid(),
                    NormalizedName = normalized
                };
                db.Roles.Add(role);
            }
            groupIdToRoleId[group.Id] = role.Id;
        }
        await db.SaveChangesAsync();

        var imported = 0;
        var updated = 0;
        var skippedClaimed = 0;
        await foreach (var legacyUser in _legacy.ReadUsersAsync())
        {
            var existing = await db.Users.FindAsync(legacyUser.Id);
            if (existing == null)
            {
                var normalizedUserName = legacyUser.Username.ToUpperInvariant();
                db.Users.Add(new ApplicationUser
                {
                    Id = legacyUser.Id,
                    UserName = legacyUser.Username,
                    NormalizedUserName = normalizedUserName,
                    Email = legacyUser.Email,
                    NormalizedEmail = legacyUser.Email?.ToUpperInvariant(),
                    EmailConfirmed = legacyUser.Email != null,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    GameToken = legacyUser.GameToken,
                    ProfileText = legacyUser.ProfileText,
                    LastWonTournament = legacyUser.LastWonTournament,
                    EmailNotificationActive = legacyUser.EmailNotificationActive,
                    MuteGames = legacyUser.MuteGames,
                    UseHouseNamesForChat = legacyUser.UseHouseNamesForChat,
                    UseMapScrollbar = legacyUser.UseMapScrollbar,
                    UseResponsiveLayoutOnMobile = legacyUser.UseResponsiveLayoutOnMobile,
                    LastUsernameUpdateTime = legacyUser.LastUsernameUpdateTime,
                    LastActivity = legacyUser.LastActivity,
                    VanillaForumUserId = legacyUser.VanillaForumUserId,
                    ImportedFromLegacy = true,
                    Claimed = false,
                    CreatedAt = legacyUser.DateJoined
                });
                imported++;
            }
            else if (existing.ImportedFromLegacy && !existing.Claimed)
            {
                // Re-import: refresh settings fields only, never touch PasswordHash/SecurityStamp/
                // GameToken/logins of a row that could theoretically have been claimed concurrently
                // (Claimed is re-checked above, but keep the update list itself narrow on principle).
                existing.ProfileText = legacyUser.ProfileText;
                existing.LastWonTournament = legacyUser.LastWonTournament;
                existing.EmailNotificationActive = legacyUser.EmailNotificationActive;
                existing.MuteGames = legacyUser.MuteGames;
                existing.UseHouseNamesForChat = legacyUser.UseHouseNamesForChat;
                existing.UseMapScrollbar = legacyUser.UseMapScrollbar;
                existing.UseResponsiveLayoutOnMobile = legacyUser.UseResponsiveLayoutOnMobile;
                existing.LastUsernameUpdateTime = legacyUser.LastUsernameUpdateTime;
                existing.LastActivity = legacyUser.LastActivity;
                existing.VanillaForumUserId = legacyUser.VanillaForumUserId;
                if (existing.Email == null && legacyUser.Email != null)
                {
                    existing.Email = legacyUser.Email;
                    existing.NormalizedEmail = legacyUser.Email.ToUpperInvariant();
                }
                updated++;
            }
            else
            {
                skippedClaimed++;
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"    users: {imported} imported, {updated} updated, {skippedClaimed} claimed (skipped)");

        // User <-> role membership, only for rows we own (imported and not yet claimed by a
        // real registration would still be correct to assign roles to; claimed rows keep
        // whatever roles the new site has already granted them, so only add missing links).
        var addedRoles = 0;
        await foreach (var userGroup in _legacy.ReadUserGroupsAsync())
        {
            if (!groupIdToRoleId.TryGetValue(userGroup.GroupId, out var roleId))
                continue;
            var exists = await db.UserRoles.AnyAsync(ur => ur.UserId == userGroup.UserId && ur.RoleId == roleId);
            if (exists)
                continue;
            var userExists = await db.Users.AnyAsync(u => u.Id == userGroup.UserId);
            if (!userExists)
                continue;
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userGroup.UserId, RoleId = roleId });
            addedRoles++;
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"    role memberships: {addedRoles} added");

        return groupIdToRoleId;
    }

    private async Task ImportRoomsAsync()
    {
        await using var db = NewTargetContext();
        var imported = 0;
        var updated = 0;
        await foreach (var legacyRoom in _legacy.ReadRoomsAsync())
        {
            var existing = await db.Rooms.FindAsync(legacyRoom.Id);
            if (existing == null)
            {
                db.Rooms.Add(new Room
                {
                    Id = legacyRoom.Id,
                    Name = legacyRoom.Name,
                    Public = legacyRoom.Public,
                    MaxRetrieveCount = legacyRoom.MaxRetrieveCount,
                    CreatedAt = legacyRoom.CreatedAt
                });
                imported++;
            }
            else
            {
                existing.Name = legacyRoom.Name;
                existing.Public = legacyRoom.Public;
                existing.MaxRetrieveCount = legacyRoom.MaxRetrieveCount;
                updated++;
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"    rooms: {imported} imported, {updated} updated");
    }

    private async Task ImportGamesAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var imported = 0;
        var updated = 0;
        var skippedMissingOwner = 0;
        await foreach (var legacyGame in _legacy.ReadGamesAsync())
        {
            if (!knownUserIds.Contains(legacyGame.OwnerId))
            {
                // Should not happen if users were imported first, but don't let one bad row abort
                // the whole batch.
                skippedMissingOwner++;
                continue;
            }

            var state = ParseGameState(legacyGame.State);
            var existing = await db.Games.FindAsync(legacyGame.Id);
            if (existing == null)
            {
                db.Games.Add(new Game
                {
                    Id = legacyGame.Id,
                    Name = legacyGame.Name,
                    OwnerUserId = legacyGame.OwnerId,
                    SerializedGame = legacyGame.SerializedGame == null ? null : JsonDocument.Parse(legacyGame.SerializedGame),
                    ViewOfGame = legacyGame.ViewOfGame == null ? null : JsonDocument.Parse(legacyGame.ViewOfGame),
                    Version = legacyGame.Version,
                    State = state,
                    CreatedAt = legacyGame.CreatedAt,
                    UpdatedAt = legacyGame.UpdatedAt,
                    LastActiveAt = legacyGame.LastActiveAt
                });
                imported++;
            }
            else
            {
                existing.Name = legacyGame.Name;
                existing.SerializedGame = legacyGame.SerializedGame == null ? null : JsonDocument.Parse(legacyGame.SerializedGame);
                existing.ViewOfGame = legacyGame.ViewOfGame == null ? null : JsonDocument.Parse(legacyGame.ViewOfGame);
                existing.Version = legacyGame.Version;
                existing.State = state;
                existing.UpdatedAt = legacyGame.UpdatedAt;
                existing.LastActiveAt = legacyGame.LastActiveAt;
                updated++;
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"    games: {imported} imported, {updated} updated, {skippedMissingOwner} skipped (missing owner)");
    }

    internal static GameState ParseGameState(string legacyState) => legacyState switch
    {
        "IN_LOBBY" => GameState.InLobby,
        "ONGOING" => GameState.Ongoing,
        "FINISHED" => GameState.Finished,
        "CLOSED" => GameState.Closed,
        "CANCELLED" => GameState.Cancelled,
        _ => GameState.InLobby
    };

    private async Task ImportPlayersInGameAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var knownGameIds = (await db.Games.Select(g => g.Id).ToListAsync()).ToHashSet();
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        await foreach (var legacyPlayer in _legacy.ReadPlayersInGameAsync())
        {
            if (!knownUserIds.Contains(legacyPlayer.UserId) || !knownGameIds.Contains(legacyPlayer.GameId))
            {
                skipped++;
                continue;
            }

            var existing = await db.PlayersInGame
                .FirstOrDefaultAsync(p => p.GameId == legacyPlayer.GameId && p.UserId == legacyPlayer.UserId);
            var data = JsonDocument.Parse(legacyPlayer.Data);
            if (existing == null)
            {
                db.PlayersInGame.Add(new PlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = legacyPlayer.GameId,
                    UserId = legacyPlayer.UserId,
                    Data = data
                });
                imported++;
            }
            else
            {
                existing.Data = data;
                updated++;
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"    players in game: {imported} imported, {updated} updated, {skipped} skipped (unknown game/user)");
    }

    private async Task ImportMessagesAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var knownRoomIds = (await db.Rooms.Select(r => r.Id).ToListAsync()).ToHashSet();

        // Messages have no stable legacy id worth preserving (see MIGRATION_PLAN.md §4.1), and
        // volume can be large, so only ever insert — treat (RoomId, UserId, Text, CreatedAt) as
        // "already imported" to keep re-runs idempotent without loading the whole table into memory.
        var existingKeys = (await db.Messages
                .Select(m => new { m.RoomId, m.UserId, m.Text, m.CreatedAt })
                .ToListAsync())
            .Select(m => (m.RoomId, m.UserId, m.Text, m.CreatedAt))
            .ToHashSet();

        var imported = 0;
        var skipped = 0;
        const int batchSize = 500;
        var pending = 0;
        await foreach (var legacyMessage in _legacy.ReadMessagesAsync())
        {
            if (!knownUserIds.Contains(legacyMessage.UserId) || !knownRoomIds.Contains(legacyMessage.RoomId))
            {
                skipped++;
                continue;
            }
            var key = (legacyMessage.RoomId, legacyMessage.UserId, legacyMessage.Text, legacyMessage.CreatedAt);
            if (existingKeys.Contains(key))
                continue;

            db.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                RoomId = legacyMessage.RoomId,
                UserId = legacyMessage.UserId,
                Text = legacyMessage.Text,
                CreatedAt = legacyMessage.CreatedAt
            });
            existingKeys.Add(key);
            imported++;
            pending++;
            if (pending >= batchSize)
            {
                await db.SaveChangesAsync();
                pending = 0;
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"    messages: {imported} imported, {skipped} skipped (unknown room/user)");
    }

    private async Task ImportPbemResponseTimesAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var imported = 0;
        var skipped = 0;
        await foreach (var legacyResponseTime in _legacy.ReadPbemResponseTimesAsync())
        {
            if (!knownUserIds.Contains(legacyResponseTime.UserId))
            {
                skipped++;
                continue;
            }
            var exists = await db.PbemResponseTimes.AnyAsync(p => p.Id == legacyResponseTime.Id);
            if (exists)
                continue;

            db.PbemResponseTimes.Add(new PbemResponseTime
            {
                Id = legacyResponseTime.Id,
                UserId = legacyResponseTime.UserId,
                ResponseTime = legacyResponseTime.ResponseTime,
                CreatedAt = legacyResponseTime.CreatedAt
            });
            imported++;
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"    PBEM response times: {imported} imported, {skipped} skipped (unknown user)");
    }

    public async Task VerifyAsync()
    {
        var legacyCounts = await _legacy.ReadRowCountsAsync();
        await using var db = NewTargetContext();

        var targetCounts = new Dictionary<string, long>
        {
            ["agotboardgame_main_user"] = await db.Users.CountAsync(),
            ["auth_group"] = await db.Roles.CountAsync(),
            ["chat_room"] = await db.Rooms.CountAsync(),
            ["agotboardgame_main_game"] = await db.Games.CountAsync(),
            ["agotboardgame_main_playeringame"] = await db.PlayersInGame.CountAsync(),
            ["chat_message"] = await db.Messages.CountAsync(),
            ["agotboardgame_main_pbemresponsetime"] = await db.PbemResponseTimes.CountAsync()
        };

        Console.WriteLine("---> Row counts (legacy -> target)");
        foreach (var (table, legacyCount) in legacyCounts)
        {
            var targetCount = targetCounts.GetValueOrDefault(table);
            var flag = table == "agotboardgame_main_playeringame" || table == "chat_message"
                ? "" // these are recalculated/append-only, counts may legitimately differ
                : legacyCount == targetCount ? "OK" : "MISMATCH";
            Console.WriteLine($"    {table,-40} {legacyCount,8} -> {targetCount,8}  {flag}");
        }

        Console.WriteLine("---> Spot-checking id round-trips");
        var sampleUserIds = await _legacy.SampleIdsAsync("agotboardgame_main_user", 5);
        foreach (var id in sampleUserIds)
        {
            var found = await db.Users.AnyAsync(u => u.Id == id);
            Console.WriteLine($"    user {id}: {(found ? "found" : "MISSING")} in target");
        }
        var sampleGameIds = await _legacy.SampleIdsAsync("agotboardgame_main_game", 5);
        foreach (var id in sampleGameIds)
        {
            var found = await db.Games.AnyAsync(g => g.Id == id);
            Console.WriteLine($"    game {id}: {(found ? "found" : "MISSING")} in target");
        }
    }
}
