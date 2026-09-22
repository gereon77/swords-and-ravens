using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// POST /api/room, DELETE /api/clearChatRoom/{roomId} — see MIGRATION_PLAN.md §6. The clear-room
/// route must be an exact literal match for LiveWebsiteClient.ts's clearChatRoom(), which calls
/// `DELETE {masterApiBaseUrl}/clearChatRoom/{roomId}` (not nested under /room/{id}/...) — this is
/// called when a faceless game starts (EntireGame.proceedToIngameGameState ->
/// GlobalServer.onClearChatRoom), so a route mismatch here throws inside the game server at the
/// start of every faceless game.
/// </summary>
public static class RoomsApi
{
    public static RouteGroupBuilder MapRoomsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization(Infrastructure.Auth.MasterApiAuthenticationHandler.SchemeName);

        group.MapPost(
            "/room",
            async (CreateRoomDto dto, ApplicationDbContext db) =>
            {
                var room = new Room
                {
                    Id = Guid.NewGuid(),
                    Name = dto.Name,
                    Public = dto.Public,
                    MaxRetrieveCount = dto.MaxRetrieveCount,
                };

                db.Rooms.Add(room);

                foreach (var userInRoom in dto.Users)
                {
                    db.UsersInRoom.Add(
                        new UserInRoom
                        {
                            Id = Guid.NewGuid(),
                            UserId = userInRoom.User,
                            RoomId = room.Id,
                        }
                    );
                }

                await db.SaveChangesAsync();
                return Results.Ok(new RoomDto(room.Id, room.Name, room.Public));
            }
        );

        group.MapDelete(
            "/clearChatRoom/{roomId:guid}",
            async (Guid roomId, ApplicationDbContext db) =>
            {
                var messages = db.Messages.Where(m => m.RoomId == roomId);
                await messages.ExecuteDeleteAsync();
                return Results.NoContent();
            }
        );

        return group;
    }
}
