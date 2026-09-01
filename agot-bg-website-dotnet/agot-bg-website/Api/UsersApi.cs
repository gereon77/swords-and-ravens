using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// GET /api/user/{id} — used by the game server to fetch a player's profile/settings. Minimal
/// API endpoint group, see MIGRATION_PLAN.md §6 (implementation note under the table).
/// </summary>
public static class UsersApi
{
    public static RouteGroupBuilder MapUsersApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/user").RequireAuthorization(Infrastructure.Auth.MasterApiAuthenticationHandler.SchemeName);

        group.MapGet("/{id:guid}", async (Guid id, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id.ToString());
            if (user is null)
            {
                return Results.NotFound();
            }

            var roles = await userManager.GetRolesAsync(user);
            var dto = new UserDto(
                user.Id,
                user.UserName ?? string.Empty,
                user.GameToken,
                roles.Contains("Admin"),
                user.MuteGames,
                user.UseHouseNamesForChat,
                user.UseMapScrollbar,
                user.GameStateColumnRight,
                (IReadOnlyList<string>)roles.ToList());

            return Results.Ok(dto);
        });

        return group;
    }
}
