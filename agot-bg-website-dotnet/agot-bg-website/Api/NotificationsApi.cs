using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// The game-server-facing notification endpoints, equivalent of Django's
/// api/views.py::notify_* + add_pbem_response_time — see MIGRATION_PLAN.md §6. Each POST body is
/// <c>{ "users": ["&lt;guid&gt;", ...] }</c>; mail is only sent to users with
/// <see cref="ApplicationUser.EmailNotificationActive"/> still on, mirroring Django's behavior.
/// Subjects/bodies are ported line-for-line from the corresponding
/// agotboardgame_main/templates/agotboardgame_main/*_notification.html templates.
/// </summary>
public static class NotificationsApi
{
    private sealed record NotifyRequest(List<Guid> Users);

    // (subject, body) builders, one per notify* route, ported line-for-line from the matching
    // agotboardgame_main/templates/agotboardgame_main/*_notification.html Django template.
    private static readonly Dictionary<
        string,
        (Func<Game, string> Subject, Func<ApplicationUser, Game, string, string> Body)
    > Templates = new()
    {
        ["notifyReadyToStart"] = (
            game => $"Your game is ready to start: {game.Name}",
            (user, game, gameUrl) =>
                $"""
                    Hello {user.UserName},

                    Your game "{game.Name}" is ready to start:

                    {gameUrl}

                    Warmest regards,
                    Staff @ Swords and Ravens
                    """
        ),
        ["notifyYourTurn"] = (
            game => $"It's your turn in '{game.Name}'",
            (user, game, gameUrl) =>
                $"""
                    Hello {user.UserName},

                    It's your turn to play in "{game.Name}":

                    {gameUrl}

                    Warmest regards,
                    Staff @ Swords and Ravens
                    """
        ),
        ["notifyBribeForSupport"] = (
            game => $"You are attacked and now can call for support in '{game.Name}'",
            (user, game, gameUrl) =>
                $"""
                    Hello {user.UserName},

                    You are attacked in the game "{game.Name}"
                    and now you can call for support or try to bribe your way there:

                    {gameUrl}

                    Warmest regards,
                    Staff @ Swords and Ravens
                    """
        ),
        ["notifyBattleResults"] = (
            game => $"Your battle is over in '{game.Name}'",
            (user, game, gameUrl) =>
                $"""
                    Hello {user.UserName},

                    Your battle in "{game.Name}" is over:

                    {gameUrl}

                    Warmest regards,
                    Staff @ Swords and Ravens
                    """
        ),
        ["notifyNewVote"] = (
            game => $"Your vote is needed in '{game.Name}'",
            (user, game, gameUrl) =>
                $"""
                    Hello {user.UserName},

                    a new vote has been started in "{game.Name}":

                    {gameUrl}

                    Warmest regards,
                    Staff @ Swords and Ravens
                    """
        ),
        ["notifyGameEnded"] = (
            game => $"Game has ended -  {game.Name}",
            (user, game, gameUrl) =>
                $"""
                    Hello {user.UserName},

                    The game "{game.Name}" has ended:

                    {gameUrl}

                    Warmest regards,
                    Staff @ Swords and Ravens
                    """
        ),
    };

    public static RouteGroupBuilder MapNotificationsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization(Infrastructure.Auth.MasterApiAuthenticationHandler.SchemeName);

        group.MapPost(
            "/addPbemResponseTime/{userId:guid}/{responseTime:int}",
            async (Guid userId, int responseTime, ApplicationDbContext db) =>
            {
                db.PbemResponseTimes.Add(
                    new PbemResponseTime
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        ResponseTime = responseTime,
                    }
                );
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
        );

        foreach (var (route, template) in Templates)
        {
            group.MapPost(
                $"/{route}/{{gameId:guid}}",
                async (
                    Guid gameId,
                    NotifyRequest body,
                    HttpContext ctx,
                    ApplicationDbContext db,
                    IEmailSender emailSender
                ) =>
                {
                    var game = await db
                        .Games.AsNoTracking()
                        .FirstOrDefaultAsync(g => g.Id == gameId);
                    if (game is null)
                    {
                        return Results.NotFound();
                    }

                    var users = await db
                        .Users.Where(u =>
                            body.Users.Contains(u.Id)
                            && u.EmailNotificationActive
                            && u.Email != null
                        )
                        .ToListAsync();

                    var request = ctx.Request;
                    var gameUrl = $"{request.Scheme}://{request.Host}/play/{gameId}";
                    var subject = template.Subject(game);

                    foreach (var user in users)
                    {
                        await emailSender.SendEmailAsync(
                            user.Email!,
                            subject,
                            template.Body(user, game, gameUrl)
                        );
                    }

                    return Results.Ok(new { status = "ok" });
                }
            );
        }

        return group;
    }
}
