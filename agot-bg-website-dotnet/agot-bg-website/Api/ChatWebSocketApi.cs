using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Infrastructure.Chat;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace agot_bg_website.Api;

/// <summary>
/// GET /ws/chat/room/{roomId} — the raw WebSocket endpoint replacing Django Channels'
/// <c>ChatConsumer</c> (chat/consumers.py). Talks the exact same JSON wire protocol as
/// <c>ChatClient.ts</c> and the preact chat widgets, so none of that client code needs to change.
/// See MIGRATION_PLAN.md §7.
/// </summary>
public static class ChatWebSocketApi
{
    // Tongueless members may only reply with a single digit, '+' or '-' — chat/consumers.py.
    // Internal (not private) so ChatWebSocketApiTests can assert its behavior directly.
    internal static readonly Regex TonguelessMessageRegex = new("^[0-9+-]$", RegexOptions.Compiled);

    /// <summary>
    /// Applies the same "cap at room's max_retrieve_count" + "count must be positive" rules the
    /// chat_retrieve handler uses before querying the DB — split out as a pure function so it's
    /// unit-testable without a real WebSocket/DbContext.
    /// </summary>
    internal static int ResolveRetrieveCount(int requestedCount, int? maxRetrieveCount) =>
        maxRetrieveCount.HasValue ? Math.Min(requestedCount, maxRetrieveCount.Value) : requestedCount;

    public static IEndpointRouteBuilder MapChatWebSocket(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/chat/room/{roomId:guid}", async (
            HttpContext context,
            Guid roomId,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            ChatConnectionManager connections,
            ChatBroadcaster broadcaster,
            ChatPresenceService presence,
            IMemoryCache memoryCache,
            IEmailSender emailSender,
            ILogger<ChatBroadcaster> logger) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var room = await db.Rooms.AsNoTracking()
                .Where(r => r.Id == roomId)
                .Select(r => new { r.Id, r.Name, r.Public, r.MaxRetrieveCount })
                .FirstOrDefaultAsync();
            if (room is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var userInRoom = await db.UsersInRoom.FirstOrDefaultAsync(u => u.UserId == user.Id && u.RoomId == roomId);
            if (!room.Public && userInRoom is null)
            {
                // Private (per-game) rooms require an existing UserInRoom row, created server-side
                // when the game/room is set up (RoomsApi) — unlike the public/issues rooms, anyone
                // authenticated may join those on first connect.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (userInRoom is null)
            {
                userInRoom = new UserInRoom { Id = Guid.NewGuid(), UserId = user.Id, RoomId = roomId };
                db.UsersInRoom.Add(userInRoom);
                await db.SaveChangesAsync();
            }

            // Presence ("connected_users") tracking only applies to the "public" room, not
            // "issues" — matches Django's ChatConsumer.connect (room_name == 'public').
            var isPresenceTrackedRoom = room.Public && room.Name == RoomSeeder.PublicRoomName;

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = connections.Add(roomId, socket, user.Id);

            try
            {
                if (isPresenceTrackedRoom)
                {
                    var userData = await GetUserDataAsync(memoryCache, userManager, user);
                    await presence.AddConnectedUserAsync(roomId, user.Id, userData);
                    await BroadcastConnectedUsersAsync(broadcaster, presence, roomId);
                }

                await ReceiveLoopAsync(
                    context, roomId, room.Name, room.Public, room.MaxRetrieveCount, user, userInRoom, socket,
                    db, userManager, broadcaster, presence, memoryCache, emailSender, logger);
            }
            finally
            {
                connections.Remove(roomId, connectionId);

                if (isPresenceTrackedRoom)
                {
                    await presence.RemoveConnectedUserAsync(roomId, user.Id);
                    await BroadcastConnectedUsersAsync(broadcaster, presence, roomId);
                }

                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    }
                    catch (WebSocketException)
                    {
                        // Best-effort — the client may have already dropped the connection.
                    }
                }

                socket.Dispose();
            }
        });

        return app;
    }

    private static async Task ReceiveLoopAsync(
        HttpContext context, Guid roomId, string roomName, bool roomPublic, int? maxRetrieveCount,
        ApplicationUser user, UserInRoom userInRoom, WebSocket socket, ApplicationDbContext db,
        UserManager<ApplicationUser> userManager, ChatBroadcaster broadcaster, ChatPresenceService presence,
        IMemoryCache memoryCache, IEmailSender emailSender, ILogger logger)
    {
        var buffer = new byte[8192];

        while (socket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);

            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(ms);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("type", out var typeElement))
                {
                    continue;
                }

                switch (typeElement.GetString())
                {
                    case "chat_message":
                        await HandleChatMessageAsync(
                            doc.RootElement, context, roomId, roomName, roomPublic, user, db, userManager,
                            broadcaster, presence, memoryCache, emailSender, logger);
                        break;
                    case "chat_view_message":
                        await HandleChatViewMessageAsync(doc.RootElement, userInRoom.Id, db);
                        break;
                    case "chat_retrieve":
                        await HandleChatRetrieveAsync(doc.RootElement, roomId, maxRetrieveCount, userInRoom.Id, socket, db);
                        break;
                }
            }
        }
    }

    private static async Task HandleChatMessageAsync(
        JsonElement data, HttpContext context, Guid roomId, string roomName, bool roomPublic, ApplicationUser user,
        ApplicationDbContext db, UserManager<ApplicationUser> userManager, ChatBroadcaster broadcaster,
        ChatPresenceService presence, IMemoryCache memoryCache, IEmailSender emailSender, ILogger logger)
    {
        var text = data.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
        if (string.IsNullOrEmpty(text) || text.Length > 200)
        {
            return;
        }

        var faceless = data.TryGetProperty("faceless", out var facelessElement) && facelessElement.GetBoolean();

        if (await userManager.IsInRoleAsync(user, RoleNames.Tongueless))
        {
            if (!TonguelessMessageRegex.IsMatch(text))
            {
                return;
            }

            var rateLimitKey = $"chat:tongueless-rate-limit:{user.Id}";
            if (memoryCache.TryGetValue(rateLimitKey, out _))
            {
                // Rate-limited to one message per 60s, mirrors Django's cache.add(..., 60).
                return;
            }

            memoryCache.Set(rateLimitKey, true, TimeSpan.FromSeconds(60));
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            UserId = user.Id,
            Text = text
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var evt = new ChatMessageEvent
        {
            Id = message.Id,
            Text = message.Text,
            UserId = user.Id,
            UserUsername = faceless ? "" : user.UserName ?? "",
            CreatedAt = message.CreatedAt
        };
        await broadcaster.PublishAsync(roomId, evt);

        if (roomPublic && (roomName == RoomSeeder.PublicRoomName || roomName == RoomSeeder.IssuesRoomName))
        {
            // Both public rooms' activity refreshes the *public* room's presence timestamp,
            // matching Django's ChatConsumer.receive_json (always refreshes public_room_id).
            await presence.RefreshLastActiveAtAsync(RoomSeeder.PublicRoomId, user.Id);
        }

        if (roomPublic)
        {
            return;
        }

        if (data.TryGetProperty("gameId", out var gameIdElement) &&
            Guid.TryParse(gameIdElement.GetString(), out var gameId))
        {
            var fromHouse = data.TryGetProperty("fromHouse", out var fromHouseElement)
                ? fromHouseElement.GetString() ?? "Unknown"
                : "Unknown";
            await NotifyChatPartnerAsync(context, db, memoryCache, emailSender, roomId, user, message, gameId, fromHouse);
        }
    }

    // Internal (not private) so ChatWebSocketApiTests can exercise the private-message email
    // notification path directly against an in-memory DbContext/cache/fake IEmailSender, without
    // needing a live WebSocket/Redis connection.
    internal static async Task NotifyChatPartnerAsync(
        HttpContext context, ApplicationDbContext db, IMemoryCache memoryCache, IEmailSender emailSender,
        Guid roomId, ApplicationUser sender, Message message, Guid gameId, string fromHouse)
    {
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId);
        if (game?.ViewOfGame is null)
        {
            return;
        }

        var pbemActive =
            game.ViewOfGame.RootElement.TryGetProperty("settings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object &&
            settings.TryGetProperty("pbem", out var pbem) &&
            pbem.ValueKind == JsonValueKind.True;
        if (!pbemActive)
        {
            return;
        }

        var otherUserInRoom = await db.UsersInRoom
            .Include(u => u.User)
            .Where(u => u.RoomId == roomId && u.UserId != sender.Id)
            .FirstOrDefaultAsync();
        var recipient = otherUserInRoom?.User;
        if (recipient is null || !recipient.EmailNotificationActive || string.IsNullOrEmpty(recipient.Email))
        {
            return;
        }

        // 7-minute de-dupe window per (room, recipient) so a burst of messages only sends one
        // email — mirrors Django's cache.has_key/cache.set(..., 7 * 60).
        var dedupeKey = $"chat:private-notify:{roomId}:{recipient.Id}";
        if (memoryCache.TryGetValue(dedupeKey, out _))
        {
            return;
        }
        memoryCache.Set(dedupeKey, true, TimeSpan.FromMinutes(7));

        var request = context.Request;
        var gameUrl = $"{request.Scheme}://{request.Host}/play/{gameId}";
        var body = $"""
            Hello {recipient.UserName},

            House {fromHouse} has sent you a raven in the game "{game.Name}":

            {message.Text}

            {gameUrl}

            Warmest regards,
            Staff @ Swords and Ravens
            """;

        await emailSender.SendEmailAsync(recipient.Email, $"You received a new private message in game: '{game.Name}'", body);
    }

    private static async Task HandleChatViewMessageAsync(JsonElement data, Guid userInRoomId, ApplicationDbContext db)
    {
        if (!data.TryGetProperty("message_id", out var messageIdElement) ||
            !Guid.TryParse(messageIdElement.GetString(), out var messageId))
        {
            return;
        }

        await db.UsersInRoom
            .Where(u => u.Id == userInRoomId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastViewedMessageId, messageId));
    }

    private static async Task HandleChatRetrieveAsync(
        JsonElement data, Guid roomId, int? maxRetrieveCount, Guid userInRoomId, WebSocket socket, ApplicationDbContext db)
    {
        var count = data.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : 0;
        count = ResolveRetrieveCount(count, maxRetrieveCount);
        if (count <= 0)
        {
            return;
        }

        var faceless = data.TryGetProperty("faceless", out var facelessElement) && facelessElement.GetBoolean();

        Guid? firstMessageId = null;
        if (data.TryGetProperty("first_message_id", out var firstMessageIdElement) &&
            firstMessageIdElement.ValueKind == JsonValueKind.String &&
            Guid.TryParse(firstMessageIdElement.GetString(), out var parsedFirstMessageId))
        {
            firstMessageId = parsedFirstMessageId;
        }

        var query = db.Messages.AsNoTracking().Where(m => m.RoomId == roomId);
        if (firstMessageId is { } fid)
        {
            var firstMessageCreatedAt = await db.Messages
                .Where(m => m.Id == fid)
                .Select(m => (DateTimeOffset?)m.CreatedAt)
                .FirstOrDefaultAsync();
            if (firstMessageCreatedAt is { } createdAt)
            {
                query = query.Where(m => m.CreatedAt < createdAt);
            }
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .Include(m => m.User)
            .ToListAsync();
        messages.Reverse(); // oldest first — mirrors Django's [0:count:-1] slice-and-reverse trick.

        Guid? lastViewedMessage = null;
        if (firstMessageId is null)
        {
            lastViewedMessage = await db.UsersInRoom
                .Where(u => u.Id == userInRoomId)
                .Select(u => u.LastViewedMessageId)
                .FirstOrDefaultAsync();
        }

        var evt = new MessagesRetrievedEvent
        {
            Type = firstMessageId is null ? "chat_messages_retrieved" : "more_chat_messages_retrieved",
            Messages = messages.Select(m => new MessageData
            {
                Id = m.Id,
                Text = m.Text,
                UserId = m.UserId,
                UserUsername = faceless ? "" : m.User?.UserName ?? "",
                CreatedAt = m.CreatedAt
            }).ToList(),
            LastViewedMessage = lastViewedMessage
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(evt);
        if (socket.State == WebSocketState.Open)
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    private static async Task BroadcastConnectedUsersAsync(ChatBroadcaster broadcaster, ChatPresenceService presence, Guid roomId)
    {
        var (users, prunedUserIds) = await presence.GetConnectedUsersAsync(roomId);

        if (prunedUserIds.Count > 0)
        {
            await broadcaster.PublishAsync(roomId, new PruneCheckEvent { UserIds = prunedUserIds });
        }

        var evt = new ConnectedUsersEvent
        {
            Users = users.ToDictionary(
                kv => kv.Key.ToString(),
                kv => new ConnectedUserWireData
                {
                    Username = kv.Value.Username,
                    IsAdmin = kv.Value.IsAdmin,
                    IsHighMember = kv.Value.IsHighMember,
                    LastWonTournament = kv.Value.LastWonTournament
                })
        };
        await broadcaster.PublishAsync(roomId, evt);
    }

    // Mirrors Django's get_user_data — cached for 5 minutes to avoid a role/DB lookup on every
    // connect/prune-check cycle.
    private static async Task<ConnectedUserData> GetUserDataAsync(
        IMemoryCache memoryCache, UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        var cacheKey = $"chat:user-data:{user.Id}";
        if (memoryCache.TryGetValue(cacheKey, out ConnectedUserData? cached) && cached is not null)
        {
            return cached;
        }

        var roles = await userManager.GetRolesAsync(user);
        var isAdmin = roles.Contains(RoleNames.Admin);
        var isHighMember = !isAdmin && roles.Contains(RoleNames.HighMember);

        var data = new ConnectedUserData(user.UserName ?? "", isAdmin, isHighMember, user.LastWonTournament);
        memoryCache.Set(cacheKey, data, TimeSpan.FromMinutes(5));
        return data;
    }
}
