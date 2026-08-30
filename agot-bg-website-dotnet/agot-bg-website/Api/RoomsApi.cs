using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>POST /api/room — see MIGRATION_PLAN.md §6.</summary>
public static class RoomsApi
{
    public static RouteGroupBuilder MapRoomsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/room").RequireAuthorization(Infrastructure.Auth.MasterApiAuthenticationHandler.SchemeName);

        group.MapPost("/", async (CreateRoomDto dto, ApplicationDbContext db) =>
        {
            var room = new Room
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                Public = dto.Public,
                MaxRetrieveCount = dto.MaxRetrieveCount
            };

            db.Rooms.Add(room);

            foreach (var userId in dto.Users)
            {
                db.UsersInRoom.Add(new UserInRoom { Id = Guid.NewGuid(), UserId = userId, RoomId = room.Id });
            }

            await db.SaveChangesAsync();
            return Results.Ok(new RoomDto(room.Id, room.Name, room.Public));
        });

        group.MapDelete("/{id:guid}/clear", async (Guid id, ApplicationDbContext db) =>
        {
            var messages = db.Messages.Where(m => m.RoomId == id);
            await messages.ExecuteDeleteAsync();
            return Results.NoContent();
        });

        return group;
    }
}
