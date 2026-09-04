using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Snr.Migration;

/// <summary>
/// Repeatable/idempotent import from the legacy Django database into the new Postgres schema.
/// Safe to re-run at any time up to and including final cutover — see MIGRATION_PLAN.md §10.
/// </summary>
public class Importer(
    string legacyConnectionString,
    string targetConnectionString,
    int messagesDaysBack = -1
)
{
    private readonly LegacyReader _legacy = new(legacyConnectionString);

    // Every Import*Async loop below periodically calls SaveChangesAsync + ChangeTracker.Clear() at
    // this granularity (rather than once at the very end of a loop that can run over 10,000+ legacy
    // rows) - without the Clear(), EF Core keeps every entity it has ever tracked in this
    // DbContext in memory for the DbContext's whole lifetime even after it's been saved, which is a
    // real risk of running out of memory on large tables. Mirrors LegacyReader's own paging of
    // reads from the legacy database for the same underlying reason.
    private const int DefaultSaveBatchSize = 500;

    /// <summary>
    /// <paramref name="progressLabel"/>/<paramref name="processedSoFar"/>/<paramref name="totalCount"/>
    /// are optional and purely cosmetic - when a label is given, an actual flush (not every call -
    /// most calls are no-ops until pendingCount reaches batchSize) prints a "still working"
    /// progress line, so a long-running step (chat_message can hold millions of rows; the games
    /// table's SerializedGame blobs make even a modest row count slow) never looks stuck.
    /// <paramref name="reportEveryNRows"/> throttles how often that line actually prints (defaults
    /// to every flush, fine for the small tables) - large tables pass a bigger value so a
    /// million-row import doesn't spam the console with one line per (small) batch.
    /// </summary>
    private static async Task<int> FlushIfBatchFullAsync(
        ApplicationDbContext db,
        int pendingCount,
        string? progressLabel = null,
        long processedSoFar = 0,
        long? totalCount = null,
        int batchSize = DefaultSaveBatchSize,
        int? reportEveryNRows = null
    )
    {
        if (pendingCount < batchSize)
        {
            return pendingCount;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        if (progressLabel != null && processedSoFar % (reportEveryNRows ?? batchSize) < batchSize)
        {
            var suffix = totalCount is { } total ? $" / {total}" : "";
            Console.WriteLine($"    ...{progressLabel}: {processedSoFar}{suffix} processed so far");
        }
        return 0;
    }

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

        Console.WriteLine("---> Importing chat messages");
        await ImportMessagesAsync();

        Console.WriteLine("---> Importing chat room memberships");
        await ImportUsersInRoomAsync();

        Console.WriteLine("---> Importing games");
        await ImportGamesAsync();

        Console.WriteLine("---> Importing current players in game");
        await ImportPlayersInGameAsync();

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
                    NormalizedName = normalized,
                };
                db.Roles.Add(role);
            }
            groupIdToRoleId[group.Id] = role.Id;
        }
        await db.SaveChangesAsync();

        var imported = 0;
        var updated = 0;
        var skippedClaimed = 0;
        var renamedDuplicates = 0;
        var processedUsers = 0;
        // Django's username is only case-sensitively unique; ASP.NET Identity's NormalizedUserName
        // is case-insensitively unique (UserNameIndex). Two legacy users differing only by case
        // (e.g. "JohnDoe" / "johndoe") would otherwise violate that index on insert. Preload every
        // NormalizedUserName already in the target (both pre-existing rows and ones this loop adds)
        // so a collision can be detected and disambiguated before it ever reaches the database -
        // purely in-memory, so periodic batch flushes below don't affect this at all.
        var usedNormalizedUserNames = (
            await db.Users.Select(u => u.NormalizedUserName!).ToListAsync()
        ).ToHashSet();
        // Counts, per base normalized username, how many collisions have already been assigned a
        // numbered suffix (starts at 1 for the first collision, which becomes "_2").
        var duplicateSuffixCounters = new Dictionary<string, int>();
        var pendingUsers = 0;
        await foreach (var legacyUser in _legacy.ReadUsersAsync())
        {
            processedUsers++;
            var existing = await db.Users.FindAsync(legacyUser.Id);
            if (existing == null)
            {
                var userName = legacyUser.Username;
                var normalizedUserName = userName.ToUpperInvariant();
                if (usedNormalizedUserNames.Contains(normalizedUserName))
                {
                    // Earliest-joined user with this normalized name (ReadUsersAsync is ordered by
                    // date_joined) keeps the plain name; every later collision gets a deterministic
                    // "_2", "_3", ... suffix so re-running the importer always produces the same
                    // disambiguated name.
                    var baseNormalizedUserName = normalizedUserName;
                    string candidateUserName;
                    string candidateNormalizedUserName;
                    do
                    {
                        var counter = duplicateSuffixCounters.GetValueOrDefault(
                            baseNormalizedUserName,
                            1
                        );
                        counter++;
                        duplicateSuffixCounters[baseNormalizedUserName] = counter;
                        candidateUserName = $"{legacyUser.Username}_{counter}";
                        candidateNormalizedUserName = candidateUserName.ToUpperInvariant();
                    } while (usedNormalizedUserNames.Contains(candidateNormalizedUserName));
                    userName = candidateUserName;
                    normalizedUserName = candidateNormalizedUserName;
                    renamedDuplicates++;
                    Console.WriteLine(
                        $"    WARNING: username '{legacyUser.Username}' (user {legacyUser.Id}) collides case-insensitively "
                            + $"with an already-imported user - renamed to '{userName}' for this import. Consider renaming "
                            + "manually later."
                    );
                }
                usedNormalizedUserNames.Add(normalizedUserName);
                db.Users.Add(
                    new ApplicationUser
                    {
                        Id = legacyUser.Id,
                        UserName = userName,
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
                        GameStateColumnRight = legacyUser.UseResponsiveLayoutOnMobile,
                        LastUsernameUpdateTime = legacyUser.LastUsernameUpdateTime,
                        LastActivity = legacyUser.LastActivity,
                        VanillaForumUserId = legacyUser.VanillaForumUserId,
                        ImportedFromLegacy = true,
                        Claimed = false,
                        CreatedAt = legacyUser.DateJoined,
                    }
                );
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
                existing.GameStateColumnRight = legacyUser.UseResponsiveLayoutOnMobile;
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
                continue;
            }
            pendingUsers = await FlushIfBatchFullAsync(
                db,
                pendingUsers + 1,
                "users",
                processedUsers
            );
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Console.WriteLine(
            $"    users: {imported} imported ({renamedDuplicates} renamed due to a case-insensitive "
                + $"username collision), {updated} updated, {skippedClaimed} claimed (skipped)"
        );

        // User <-> role membership, only for rows we own (imported and not yet claimed by a
        // real registration would still be correct to assign roles to; claimed rows keep
        // whatever roles the new site has already granted them, so only add missing links).
        var addedRoles = 0;
        var pendingRoles = 0;
        var processedRoles = 0;
        await foreach (var userGroup in _legacy.ReadUserGroupsAsync())
        {
            processedRoles++;
            if (!groupIdToRoleId.TryGetValue(userGroup.GroupId, out var roleId))
                continue;
            var exists = await db.UserRoles.AnyAsync(ur =>
                ur.UserId == userGroup.UserId && ur.RoleId == roleId
            );
            if (exists)
                continue;
            var userExists = await db.Users.AnyAsync(u => u.Id == userGroup.UserId);
            if (!userExists)
                continue;
            db.UserRoles.Add(
                new IdentityUserRole<Guid> { UserId = userGroup.UserId, RoleId = roleId }
            );
            addedRoles++;
            pendingRoles = await FlushIfBatchFullAsync(
                db,
                pendingRoles + 1,
                "role memberships",
                processedRoles
            );
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Console.WriteLine($"    role memberships: {addedRoles} added");

        return groupIdToRoleId;
    }

    private async Task ImportRoomsAsync()
    {
        await using var db = NewTargetContext();
        var imported = 0;
        var updated = 0;
        var pending = 0;
        var processed = 0;
        await foreach (var legacyRoom in _legacy.ReadRoomsAsync())
        {
            processed++;

            var existing = await db.Rooms.FindAsync(legacyRoom.Id);
            if (existing == null)
            {
                db.Rooms.Add(
                    new Room
                    {
                        Id = legacyRoom.Id,
                        Name = legacyRoom.Name,
                        Public = legacyRoom.Public,
                        MaxRetrieveCount = legacyRoom.MaxRetrieveCount,
                        CreatedAt = legacyRoom.CreatedAt,
                    }
                );
                imported++;
            }
            else
            {
                existing.Name = legacyRoom.Name;
                existing.Public = legacyRoom.Public;
                existing.MaxRetrieveCount = legacyRoom.MaxRetrieveCount;
                updated++;
            }
            pending = await FlushIfBatchFullAsync(db, pending + 1, "rooms", processed);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Console.WriteLine($"    rooms: {imported} imported, {updated} updated");
    }

    /// <summary>
    /// Backfills chat_userinroom (Django's UserInRoom, see chat/models.py) — its absence here was
    /// the root cause of every migrated private chat room being permanently unjoinable, since
    /// ChatWebSocketApi.cs requires an existing UserInRoom row before it will let anyone connect
    /// to a non-public room. Must run after ImportMessagesAsync so LastViewedMessageId's target
    /// value (now the same id as the legacy one - see LegacyMessage's doc comment) is guaranteed
    /// to already exist. Keyed on (UserId, RoomId) to stay idempotent on re-runs, and - unlike the
    /// insert-only approach this used to take - LastViewedMessageId is updated on every re-run
    /// too, so re-running against a fresher legacy snapshot before final cutover picks up newer
    /// "unread" markers instead of freezing them at whatever they were on the first run.
    /// </summary>
    private async Task ImportUsersInRoomAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var knownRoomIds = (await db.Rooms.Select(r => r.Id).ToListAsync()).ToHashSet();
        var knownMessageIds = (await db.Messages.Select(m => m.Id).ToListAsync()).ToHashSet();
        var existingIds = await db
            .UsersInRoom.Select(u => new
            {
                u.UserId,
                u.RoomId,
                u.Id,
            })
            .ToDictionaryAsync(u => (u.UserId, u.RoomId), u => u.Id);

        // Tracks entities this method has itself Added/Attached since the last SaveChanges +
        // ChangeTracker.Clear() flush (FlushIfBatchFullAsync returning 0 signals that a flush just
        // happened). The legacy chat_room_membership table turns out not to be reliably unique on
        // (user, room) - a handful of rows repeat the same pair - so a duplicate seen again before
        // the next flush must update the *same* tracked instance instead of Attach()-ing a second
        // stub with the same Id, which EF Core rejects with an identity-conflict exception.
        var trackedThisBatch = new Dictionary<(Guid UserId, Guid RoomId), UserInRoom>();

        var imported = 0;
        var updated = 0;
        var duplicates = 0;
        var skipped = 0;
        var pending = 0;
        var processed = 0;
        await foreach (var legacyUserInRoom in _legacy.ReadUsersInRoomAsync())
        {
            processed++;
            if (
                !knownUserIds.Contains(legacyUserInRoom.UserId)
                || !knownRoomIds.Contains(legacyUserInRoom.RoomId)
            )
            {
                skipped++;
                continue;
            }

            // A room's last-viewed marker can point at a message this pass hasn't imported (e.g.
            // it's outside --messages-days-back's window) - fall back to null rather than fail the
            // whole row, since losing an "unread" marker is harmless (worst case: a message that
            // was actually already read shows as unread again).
            var lastViewedMessageId =
                legacyUserInRoom.LastViewedMessageId is { } messageId
                && knownMessageIds.Contains(messageId)
                    ? messageId
                    : (long?)null;

            var key = (legacyUserInRoom.UserId, legacyUserInRoom.RoomId);

            if (trackedThisBatch.TryGetValue(key, out var trackedEntity))
            {
                trackedEntity.LastViewedMessageId = lastViewedMessageId;
                duplicates++;
            }
            else if (existingIds.TryGetValue(key, out var existingId))
            {
                var stub = new UserInRoom
                {
                    Id = existingId,
                    LastViewedMessageId = lastViewedMessageId,
                };
                db.UsersInRoom.Attach(stub);
                db.Entry(stub).Property(u => u.LastViewedMessageId).IsModified = true;
                trackedThisBatch[key] = stub;
                updated++;
            }
            else
            {
                var entity = new UserInRoom
                {
                    Id = Guid.NewGuid(),
                    UserId = legacyUserInRoom.UserId,
                    RoomId = legacyUserInRoom.RoomId,
                    LastViewedMessageId = lastViewedMessageId,
                };
                db.UsersInRoom.Add(entity);
                existingIds[key] = entity.Id;
                trackedThisBatch[key] = entity;
                imported++;
            }
            pending = await FlushIfBatchFullAsync(
                db,
                pending + 1,
                "chat room memberships",
                processed
            );
            if (pending == 0)
            {
                // A flush just cleared the change tracker - nothing tracked before this point can
                // be reused anymore.
                trackedThisBatch.Clear();
            }
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Console.WriteLine(
            $"    chat room memberships: {imported} imported, {updated} updated, {duplicates} duplicate rows merged, {skipped} skipped"
        );
    }

    private async Task ImportGamesAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var totalGames = await _legacy.CountGamesAsync();
        Console.WriteLine($"    games: {totalGames} total in legacy database, importing...");

        // Preloaded once so the historical PreviousPlayerInGame backfill (computed inline below,
        // right while each game's ViewOfGame is already parsed in memory - no second pass over
        // Games afterwards) never overwrites rows a genuine live game-server save already wrote.
        var gameIdsWithExistingPreviousPlayers = (
            await db.PreviousPlayersInGame.Select(p => p.GameId).Distinct().ToListAsync()
        ).ToHashSet();

        // Preloaded once (separate small read of agotboardgame_main_playeringame, no large blobs)
        // so the backfill below can tell whether a user named in ViewOfGame's oldPlayerIds/
        // timeoutPlayerIds is still a current player - ViewOfGame itself has no raw list of current
        // player user-ids (only victoryTrack[].player, a display username, not a userId) - see
        // PreviousPlayersBackfill.cs's doc comment. ImportPlayersInGameAsync (called later in
        // RunAsync) reads this same legacy table again to actually persist PlayersInGame rows; the
        // duplicate read is cheap since this table carries no large per-row blobs.
        var currentPlayersByGame = new Dictionary<Guid, HashSet<Guid>>();
        await foreach (var legacyPlayer in _legacy.ReadPlayersInGameAsync())
        {
            if (!currentPlayersByGame.TryGetValue(legacyPlayer.GameId, out var set))
            {
                set = [];
                currentPlayersByGame[legacyPlayer.GameId] = set;
            }
            set.Add(legacyPlayer.UserId);
        }

        // Preloaded once so games whose SerializedGame blob is already stored locally never have
        // that (potentially multi-MB) column transferred from the legacy database again on a
        // re-run - see the targeted fetch further below and ReadSerializedGamesByIdsAsync's doc
        // comment. Assumes a game's SerializedGame is immutable once captured, which is exactly
        // true for the one-off final cutover import (run only after the production site is
        // stopped) and true in practice for Finished/Cancelled games otherwise.
        var gameIdsWithSerializedGame = (
            await db.Games.Where(g => g.SerializedGame != null).Select(g => g.Id).ToListAsync()
        ).ToHashSet();

        var imported = 0;
        var updated = 0;
        var skippedMissingOwner = 0;
        var skippedCancelledLobby = 0;
        var deletedCancelledLobby = 0;
        var backfilledRows = 0;
        var backfilledGames = 0;

        // SerializedGame can be multi-MB per row and is never inspected by this importer (only
        // ViewOfGame's small top-level fields are ever read - see Game's doc comment), so it's
        // deliberately never round-tripped through JsonDocument.Parse/the EF value converter here:
        // that would mean parsing (and holding in memory) a full JSON DOM for every single game for
        // no benefit. Instead its raw text is written straight through via a batched raw SQL
        // UPDATE (WriteSerializedGamesRawAsync) - Postgres itself validates/stores the jsonb value
        // - once the row already exists (i.e. right after the batch's own SaveChangesAsync, which
        // handles every other column). This batch size is intentionally much smaller than
        // LegacyReader's own (much lighter, blob-free) Games page size - it bounds how much raw
        // JSON text a single targeted ReadSerializedGamesByIdsAsync call/batch holds in memory at
        // once, since any individual SerializedGame can still be several MB. It also lets
        // ChangeTracker.Clear() release the batch's tracked entities before moving on - see
        // FlushIfBatchFullAsync's doc comment for why that matters at all on a table this size.
        const int gamesBatchSize = 20;
        const int gamesReportEveryNRows = 1000;
        var processedGames = 0;
        var pendingSerializedGames = new List<(Guid GameId, string SerializedGame)>();
        // Game ids in the current batch whose SerializedGame still needs to be fetched from the
        // legacy database at all - a targeted, batch-sized query (see FlushGamesBatchAsync) rather
        // than the wide per-page read, so re-runs only ever pay for blobs that are actually new.
        var idsNeedingBlob = new List<Guid>();
        var pendingInBatch = 0;

        async Task FlushGamesBatchAsync()
        {
            if (pendingInBatch == 0)
            {
                return;
            }
            await db.SaveChangesAsync();
            if (idsNeedingBlob.Count > 0)
            {
                var blobs = await _legacy.ReadSerializedGamesByIdsAsync(idsNeedingBlob);
                foreach (var (gameId, serializedGame) in blobs)
                {
                    if (serializedGame is not null)
                    {
                        pendingSerializedGames.Add((gameId, serializedGame));
                    }
                }
                idsNeedingBlob.Clear();
            }
            await WriteSerializedGamesRawAsync(db, pendingSerializedGames);
            db.ChangeTracker.Clear();
            pendingSerializedGames.Clear();
            pendingInBatch = 0;
            if (processedGames % gamesReportEveryNRows < gamesBatchSize)
            {
                Console.WriteLine(
                    $"    ...games: {processedGames} / {totalGames} processed so far"
                );
            }
        }

        await foreach (var legacyGame in _legacy.ReadGamesAsync())
        {
            processedGames++;
            if (!knownUserIds.Contains(legacyGame.OwnerId))
            {
                // Should not happen if users were imported first, but don't let one bad row abort
                // the whole batch.
                skippedMissingOwner++;
                continue;
            }

            var state = ParseGameState(legacyGame.State);

            var viewOfGame =
                legacyGame.ViewOfGame == null ? null : JsonDocument.Parse(legacyGame.ViewOfGame);

            // A game cancelled before it ever left the lobby (view_of_game.turn still -1, i.e. it
            // never even finished drafting/setup) has nothing worth keeping - no players ever
            // really played, no chat worth preserving. The live save-game endpoint (GamesApi.cs)
            // now deletes such games outright as soon as they're cancelled; mirror that here so a
            // fresh import never creates one in the first place (skip it before persisting
            // anything for this game at all).
            if (state == GameState.Cancelled && IsTurnMinusOne(viewOfGame))
            {
                skippedCancelledLobby++;
                // Rare cleanup-only path (a previous run imported this game before this rule
                // existed) - loading the full row here (including SerializedGame) is fine since
                // it's the exception rather than every game.
                var existingCancelled = await db.Games.FirstOrDefaultAsync(g =>
                    g.Id == legacyGame.Id
                );
                if (existingCancelled is not null)
                {
                    // Was imported by an older run before this rule existed - clean it up now,
                    // same as a fresh import would (never create it in the first place).
                    await DeleteCancelledLobbyGameAsync(db, existingCancelled);
                    deletedCancelledLobby++;
                    db.ChangeTracker.Clear();
                }
                continue;
            }

            // AnyAsync (not FindAsync) deliberately avoids transferring/materializing the existing
            // row's columns - in particular SerializedGame - just to check whether it exists.
            var alreadyExists = await db.Games.AnyAsync(g => g.Id == legacyGame.Id);
            if (!alreadyExists)
            {
                // SerializedGame intentionally left unset (defaults to null) - see the batch
                // comment above; it's written separately, right after this row exists.
                db.Games.Add(
                    new Game
                    {
                        Id = legacyGame.Id,
                        Name = legacyGame.Name,
                        OwnerUserId = legacyGame.OwnerId,
                        ViewOfGame = viewOfGame,
                        Version = legacyGame.Version,
                        State = state,
                        CreatedAt = legacyGame.CreatedAt,
                        UpdatedAt = legacyGame.UpdatedAt,
                        LastActiveAt = legacyGame.LastActiveAt,
                    }
                );
                imported++;
            }
            else
            {
                // Attach-a-stub-and-mark-modified instead of FindAsync + assign: same reasoning as
                // AnyAsync above - avoids ever loading (and JsonDocument-parsing) the existing row's
                // SerializedGame column just to overwrite it a moment later. OwnerUserId/CreatedAt
                // are deliberately not marked modified (never updated once set, same as before).
                var stub = new Game
                {
                    Id = legacyGame.Id,
                    Name = legacyGame.Name,
                    ViewOfGame = viewOfGame,
                    Version = legacyGame.Version,
                    State = state,
                    UpdatedAt = legacyGame.UpdatedAt,
                    LastActiveAt = legacyGame.LastActiveAt,
                };
                db.Games.Attach(stub);
                var entry = db.Entry(stub);
                entry.Property(g => g.Name).IsModified = true;
                entry.Property(g => g.ViewOfGame).IsModified = true;
                entry.Property(g => g.Version).IsModified = true;
                entry.Property(g => g.State).IsModified = true;
                entry.Property(g => g.UpdatedAt).IsModified = true;
                entry.Property(g => g.LastActiveAt).IsModified = true;
                updated++;
            }

            // Only fetch this game's SerializedGame blob from the legacy database at all if we
            // don't already have one stored locally - see gameIdsWithSerializedGame's preload
            // comment above. A brand-new game always needs it (alreadyExists is false).
            if (!alreadyExists || !gameIdsWithSerializedGame.Contains(legacyGame.Id))
            {
                idsNeedingBlob.Add(legacyGame.Id);
            }

            // Historical PreviousPlayerInGame backfill (§10.1) - computed right here while
            // viewOfGame is already parsed in memory, rather than re-querying every Game a second
            // time afterwards. Only Finished/Cancelled games can have removed players worth
            // recording (InLobby/Ongoing games haven't concluded - any removal so far is still
            // "live" data the game server itself will keep saving via GamesApi.cs). Never touches a
            // game that already has rows - see the field's preload above.
            if (
                (state == GameState.Finished || state == GameState.Cancelled)
                && viewOfGame is not null
                && !gameIdsWithExistingPreviousPlayers.Contains(legacyGame.Id)
            )
            {
                var previousPlayers = PreviousPlayersBackfill.Compute(
                    legacyGame.Id,
                    viewOfGame,
                    currentPlayersByGame.GetValueOrDefault(legacyGame.Id, [])
                );
                if (previousPlayers.Count > 0)
                {
                    db.PreviousPlayersInGame.AddRange(previousPlayers);
                    backfilledRows += previousPlayers.Count;
                    backfilledGames++;
                }
            }

            pendingInBatch++;
            if (pendingInBatch >= gamesBatchSize)
            {
                await FlushGamesBatchAsync();
            }
        }
        await FlushGamesBatchAsync();
        Console.WriteLine(
            $"    games: {imported} imported, {updated} updated, {skippedMissingOwner} skipped (missing owner), "
                + $"{skippedCancelledLobby} skipped (cancelled lobby games, never migrated), "
                + $"{deletedCancelledLobby} previously-imported cancelled-lobby games deleted"
        );
        Console.WriteLine(
            $"    previous players in game (historical backfill): {backfilledRows} rows backfilled across {backfilledGames} games"
        );
    }

    /// <summary>
    /// Writes each game's SerializedGame column via a raw parameterized UPDATE, batched into a
    /// single NpgsqlBatch round trip - deliberately bypasses JsonDocument.Parse/the EF value
    /// converter entirely (see ImportGamesAsync's comment for why) by passing the original text
    /// straight through as an <see cref="NpgsqlDbType.Jsonb"/> parameter and letting Postgres
    /// itself validate/store it. Requires every referenced GameId to already exist as a row - the
    /// batch's own SaveChangesAsync (which inserts/updates every other column) must run first.
    /// </summary>
    private static async Task WriteSerializedGamesRawAsync(
        ApplicationDbContext db,
        List<(Guid GameId, string SerializedGame)> pending
    )
    {
        if (pending.Count == 0)
        {
            return;
        }

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        // Opened once and left open across batches (not explicitly closed here) so it's reused for
        // every subsequent batch's raw write instead of paying an open/close round trip each time;
        // `db`'s disposal at the end of ImportGamesAsync closes it for good.
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var batch = new NpgsqlBatch(connection);
        foreach (var (gameId, serializedGame) in pending)
        {
            var command = new NpgsqlBatchCommand(
                """UPDATE "Games" SET "SerializedGame" = @sg WHERE "Id" = @id"""
            );
            command.Parameters.Add(
                new NpgsqlParameter("sg", NpgsqlDbType.Jsonb) { Value = serializedGame }
            );
            command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = gameId });
            batch.BatchCommands.Add(command);
        }
        await batch.ExecuteNonQueryAsync();
    }

    /// <summary>Reads the `turn` field out of an already-parsed `view_of_game` JSON document.</summary>
    internal static bool IsTurnMinusOne(JsonDocument? viewOfGame) =>
        viewOfGame is not null
        && viewOfGame.RootElement.TryGetProperty("turn", out var turnEl)
        && turnEl.ValueKind == JsonValueKind.Number
        && turnEl.GetInt32() == -1;

    private static async Task DeleteCancelledLobbyGameAsync(ApplicationDbContext db, Game game)
    {
        // Resolve the public chat room the same way the live save-game endpoint does
        // (view_of_game.publicChatRoomId, falling back to serialized_game's top-level field for
        // older saves where view_of_game may not have carried it yet).
        var roomId =
            TryGetPublicChatRoomId(game.ViewOfGame) ?? TryGetPublicChatRoomId(game.SerializedGame);
        if (roomId is { } id)
        {
            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
            if (room is not null)
            {
                db.Rooms.Remove(room); // cascades to Messages/UsersInRoom
            }
        }
        db.Games.Remove(game); // cascades to PlayersInGame/PreviousPlayersInGame
    }

    private static Guid? TryGetPublicChatRoomId(JsonDocument? doc) =>
        doc is not null
        && doc.RootElement.TryGetProperty("publicChatRoomId", out var el)
        && el.ValueKind == JsonValueKind.String
        && Guid.TryParse(el.GetString(), out var id)
            ? id
            : null;

    internal static GameState ParseGameState(string legacyState) =>
        legacyState switch
        {
            "IN_LOBBY" => GameState.InLobby,
            "ONGOING" => GameState.Ongoing,
            "FINISHED" => GameState.Finished,
            "CLOSED" => GameState.Closed,
            "CANCELLED" => GameState.Cancelled,
            _ => GameState.InLobby,
        };

    private async Task ImportPlayersInGameAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var knownGameIds = (await db.Games.Select(g => g.Id).ToListAsync()).ToHashSet();

        // Preloaded once (Id only, never the Data blob) so every row's existence can be checked
        // purely in memory. This is necessary for correctness, not just performance: the legacy
        // agotboardgame_main_playeringame table turns out to contain duplicate (game_id, user_id)
        // rows for some games, and a per-row FirstOrDefaultAsync query against the database (as
        // this used to do) cannot see a row this same loop has already Add-ed but not yet flushed -
        // so the second occurrence of a duplicate pair within one batch would be inserted again
        // instead of updated, violating the unique index. existingIds is kept up to date with every
        // insert below so any later duplicate (even across a batch boundary) is always treated as
        // an update instead.
        var existingIds = await db
            .PlayersInGame.Select(p => new
            {
                p.GameId,
                p.UserId,
                p.Id,
            })
            .ToDictionaryAsync(p => (p.GameId, p.UserId), p => p.Id);

        // Entities currently tracked by this DbContext, keyed the same way, so a duplicate pair
        // seen again before the next flush updates the very same tracked instance rather than
        // attaching a second instance with the same key (which EF Core would reject outright).
        // Must be cleared every time FlushIfBatchFullAsync actually flushes (ChangeTracker.Clear()
        // detaches everything), which is why the loop checks its return value below instead of
        // just reusing the local `pending` variable.
        var trackedThisBatch = new Dictionary<(Guid GameId, Guid UserId), PlayerInGame>();

        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var duplicates = 0;
        var pending = 0;
        var processed = 0;
        await foreach (var legacyPlayer in _legacy.ReadPlayersInGameAsync())
        {
            processed++;
            if (
                !knownUserIds.Contains(legacyPlayer.UserId)
                || !knownGameIds.Contains(legacyPlayer.GameId)
            )
            {
                skipped++;
                continue;
            }

            var key = (legacyPlayer.GameId, legacyPlayer.UserId);
            var data = JsonDocument.Parse(legacyPlayer.Data);
            if (trackedThisBatch.TryGetValue(key, out var tracked))
            {
                tracked.Data = data;
                duplicates++;
            }
            else if (existingIds.TryGetValue(key, out var existingId))
            {
                var stub = new PlayerInGame { Id = existingId, Data = data };
                db.PlayersInGame.Attach(stub);
                db.Entry(stub).Property(p => p.Data).IsModified = true;
                trackedThisBatch[key] = stub;
                updated++;
            }
            else
            {
                var entity = new PlayerInGame
                {
                    Id = Guid.NewGuid(),
                    GameId = legacyPlayer.GameId,
                    UserId = legacyPlayer.UserId,
                    Data = data,
                };
                db.PlayersInGame.Add(entity);
                existingIds[key] = entity.Id;
                trackedThisBatch[key] = entity;
                imported++;
            }

            var flushed = await FlushIfBatchFullAsync(
                db,
                pending + 1,
                "players in game",
                processed
            );
            if (flushed == 0)
            {
                trackedThisBatch.Clear();
            }
            pending = flushed;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Console.WriteLine(
            $"    players in game: {imported} imported, {updated} updated, {skipped} skipped (unknown game/user)"
                + (
                    duplicates > 0
                        ? $", {duplicates} duplicate legacy rows collapsed (last one wins)"
                        : ""
                )
        );
    }

    /// <summary>
    /// Message.Id now preserves chat_message.id exactly instead of generating a fresh Guid (see
    /// LegacyMessage's doc comment), so idempotency is keyed on that id directly rather than the
    /// (RoomId, UserId, Text, CreatedAt) natural key this used to rely on. EF Core's Npgsql
    /// provider defaults long PKs to "GENERATED BY DEFAULT AS IDENTITY", which allows inserting
    /// these explicit ids - but doing so never advances the backing sequence, so every run (even
    /// one that imports nothing new) ends by bumping it to MAX(Id), otherwise the next message
    /// sent live through the app could collide with an id this import already claimed.
    /// </summary>
    private async Task ImportMessagesAsync()
    {
        if (messagesDaysBack == 0)
        {
            Console.WriteLine("    messages: 0 imported (messagesDaysBack=0, import disabled)");
            return;
        }

        DateTimeOffset? sinceUtc =
            messagesDaysBack > 0 ? DateTimeOffset.UtcNow.AddDays(-messagesDaysBack) : null;

        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        var knownRoomIds = (await db.Rooms.Select(r => r.Id).ToListAsync()).ToHashSet();
        var knownMessageIds = (await db.Messages.Select(m => m.Id).ToListAsync()).ToHashSet();
        var totalMessages = await _legacy.CountMessagesAsync(sinceUtc);
        Console.WriteLine($"    messages: {totalMessages} total in legacy database, importing...");

        var imported = 0;
        var skipped = 0;
        var pending = 0;
        var processed = 0;
        await foreach (var legacyMessage in _legacy.ReadMessagesAsync(sinceUtc))
        {
            processed++;
            if (
                !knownUserIds.Contains(legacyMessage.UserId)
                || !knownRoomIds.Contains(legacyMessage.RoomId)
            )
            {
                skipped++;
                continue;
            }
            if (knownMessageIds.Contains(legacyMessage.Id))
                continue;

            db.Messages.Add(
                new Message
                {
                    Id = legacyMessage.Id,
                    RoomId = legacyMessage.RoomId,
                    UserId = legacyMessage.UserId,
                    Text = legacyMessage.Text,
                    CreatedAt = legacyMessage.CreatedAt,
                }
            );
            knownMessageIds.Add(legacyMessage.Id);
            imported++;
            // ChangeTracker.Clear() (not just SaveChangesAsync) is the important part here: without
            // it every Message entity ever Added stays tracked for the DbContext's whole lifetime
            // even once saved, which is the real OOM risk on a table that can hold millions of rows
            // - see FlushIfBatchFullAsync's doc comment. reportEveryNRows is much larger than the
            // 500-row save batch size so a 2-million-row import doesn't print 4,000 lines.
            pending = await FlushIfBatchFullAsync(
                db,
                pending + 1,
                "messages",
                processed,
                totalMessages,
                reportEveryNRows: 100_000
            );
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Always run, even if imported == 0: a previous run may have inserted explicit ids without
        // ever bumping the sequence past them (e.g. the process was interrupted before reaching
        // this point), so this must be unconditional rather than only-after-a-successful-import.
        await db.Database.ExecuteSqlRawAsync(
            """
            SELECT setval(
                pg_get_serial_sequence('"Messages"', 'Id'),
                COALESCE((SELECT MAX("Id") FROM "Messages"), 0)
            )
            """
        );

        Console.WriteLine(
            $"    messages: {imported} imported, {skipped} skipped (unknown room/user)"
        );
    }

    private async Task ImportPbemResponseTimesAsync()
    {
        await using var db = NewTargetContext();
        var knownUserIds = (await db.Users.Select(u => u.Id).ToListAsync()).ToHashSet();
        // Preloaded once instead of one AnyAsync round-trip per legacy row - this table can hold
        // over a million rows, so a per-row query would mean well over a million network
        // round-trips. Id is the legacy table's real primary key, so no duplicate-row concerns
        // like ImportPlayersInGameAsync has - a plain HashSet is enough here.
        var existingIds = (await db.PbemResponseTimes.Select(p => p.Id).ToListAsync()).ToHashSet();
        var imported = 0;
        var skipped = 0;
        var pending = 0;
        var processed = 0;
        await foreach (var legacyResponseTime in _legacy.ReadPbemResponseTimesAsync())
        {
            processed++;
            if (!knownUserIds.Contains(legacyResponseTime.UserId))
            {
                skipped++;
                continue;
            }
            if (!existingIds.Add(legacyResponseTime.Id))
                continue;

            db.PbemResponseTimes.Add(
                new PbemResponseTime
                {
                    Id = legacyResponseTime.Id,
                    UserId = legacyResponseTime.UserId,
                    ResponseTime = legacyResponseTime.ResponseTime,
                    CreatedAt = legacyResponseTime.CreatedAt,
                }
            );
            imported++;
            pending = await FlushIfBatchFullAsync(
                db,
                pending + 1,
                "PBEM response times",
                processed,
                reportEveryNRows: 50_000
            );
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Console.WriteLine(
            $"    PBEM response times: {imported} imported, {skipped} skipped (unknown user)"
        );
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
            ["agotboardgame_main_pbemresponsetime"] = await db.PbemResponseTimes.CountAsync(),
        };

        Console.WriteLine("---> Row counts (legacy -> target)");
        foreach (var (table, legacyCount) in legacyCounts)
        {
            var targetCount = targetCounts.GetValueOrDefault(table);
            var flag =
                table == "agotboardgame_main_playeringame" || table == "chat_message" ? "" // these are recalculated/append-only, counts may legitimately differ
                : legacyCount == targetCount ? "OK"
                : "MISMATCH";
            Console.WriteLine($"    {table, -40} {legacyCount, 8} -> {targetCount, 8}  {flag}");
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
