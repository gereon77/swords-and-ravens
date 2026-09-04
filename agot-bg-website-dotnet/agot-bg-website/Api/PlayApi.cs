using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// GET /play/{gameId}/{userId?} — serves the game client, injecting the auth payload the TS
/// client needs to open its own WebSocket connection to the game server. Equivalent of Django's
/// <c>agotboardgame_main.views.play</c> — see MIGRATION_PLAN.md §8.3. (Implemented as a Minimal
/// API endpoint, not an MVC controller, to stay consistent with the rest of this app's REST
/// surface — GamesApi/RoomsApi/etc. are Minimal APIs too.)
/// </summary>
public static class PlayApi
{
    // Populated by build_and_place_game_client_into_dotnet.ps1/.sh from agot-bg-game-server/dist,
    // see MIGRATION_PLAN.md §8.1/§8.2. Falls back to the fake template so `dotnet run` alone still
    // boots a usable (if game-less) site, same developer experience Django provides today.
    private static readonly string TemplatesDir = Path.Combine(
        AppContext.BaseDirectory,
        "GameClientTemplates"
    );
    private static readonly string RealTemplatePath = Path.Combine(TemplatesDir, "play.html");
    private static readonly string FakeTemplatePath = Path.Combine(TemplatesDir, "play_fake.html");

    private const string AuthDataPlaceholder = "AUTH_DATA_JSON";

    public static RouteGroupBuilder MapPlayApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/play").RequireAuthorization();

        group.MapGet(
            "/{gameId:guid}/{userId:guid?}",
            async (
                Guid gameId,
                Guid? userId,
                HttpContext ctx,
                ApplicationDbContext db,
                UserManager<ApplicationUser> userManager,
                SignInManager<ApplicationUser> signInManager,
                IAuthorizationService authorizationService
            ) =>
            {
                var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId);
                if (game is null)
                {
                    return Results.NotFound();
                }

                var requestUser = await userManager.GetUserAsync(ctx.User);
                if (requestUser is null)
                {
                    return Results.Unauthorized();
                }

                if (await userManager.IsInRoleAsync(requestUser, RoleNames.Banned))
                {
                    // Force logout of banned members, same as Django's views.play.
                    await signInManager.SignOutAsync();
                    return Results.Redirect("/games");
                }

                if (
                    game.State == GameState.InLobby
                    && await userManager.IsInRoleAsync(requestUser, RoleNames.OnProbation)
                )
                {
                    var alreadyInGame = await db.PlayersInGame.AnyAsync(p =>
                        p.GameId == gameId && p.UserId == requestUser.Id
                    );
                    if (!alreadyInGame)
                    {
                        // Members on probation can't join new lobby games, but can rejoin ones they're
                        // already in and spectate ongoing/finished/cancelled games.
                        return Results.Redirect("/games");
                    }
                }

                var effectiveUser = requestUser;
                if (userId is { } impersonateId)
                {
                    var canImpersonate = (
                        await authorizationService.AuthorizeAsync(
                            ctx.User,
                            GamePermissions.ImpersonateOtherPlayers
                        )
                    ).Succeeded;

                    if (canImpersonate)
                    {
                        var impersonationFailure = await TryApplyImpersonationAsync(impersonateId);
                        if (impersonationFailure is not null)
                        {
                            return impersonationFailure;
                        }
                    }
                }


                async Task<IResult?> TryApplyImpersonationAsync(Guid impersonateId)
                {
                    var alreadyPlaying = await db.PlayersInGame.AnyAsync(p =>
                        p.GameId == gameId && p.UserId == requestUser.Id
                    );

                    if (alreadyPlaying)
                    {
                        var isAdmin = await userManager.IsInRoleAsync(requestUser, RoleNames.Admin);
                        if (!isAdmin)
                        {
                            // Non-admin users cannot impersonate other players of games where they participate.
                            effectiveUser = requestUser;
                            return null;
                        }
                    }

                    var impersonated = await userManager.FindByIdAsync(impersonateId.ToString());
                    if (impersonated is null)
                    {
                        return Results.NotFound();
                    }

                    effectiveUser = impersonated;
                    return null;
                }

                var authData = new
                {
                    userId = effectiveUser.Id,
                    requestUserId = requestUser.Id,
                    gameId,
                    authToken = effectiveUser.GameToken,
                };

                // System.Text.Json's default encoder already HTML/script-safe-escapes '<', '>', '&'
                // etc., equivalent to what Django's json_script does before embedding JSON in a
                // <script> tag.
                var json = JsonSerializer.Serialize(authData);
                var template = File.Exists(RealTemplatePath)
                    ? await File.ReadAllTextAsync(RealTemplatePath)
                    : await File.ReadAllTextAsync(FakeTemplatePath);

                var html = template.Replace(AuthDataPlaceholder, json);

                return Results.Content(html, "text/html");
            }
        );

        return group;
    }
}
