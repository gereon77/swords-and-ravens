using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using System.Net;

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

    // (subject, body) builders, one per notify* route, preserving the wording/structure from the
    // matching agotboardgame_main/templates/agotboardgame_main/*_notification.html Django
    // template, but rendered as actual HTML because SMTP sends them with IsBodyHtml=true.
    private static readonly Dictionary<
        string,
        (Func<Game, string> Subject, Func<ApplicationUser, Game, string, string> Body)
    > Templates = new()
    {
        ["notifyReadyToStart"] = (
            game => $"Your game is ready to start: {game.Name}",
            (user, game, gameUrl) =>
                BuildNotificationEmailHtml(
                    user.UserName,
                    $"Your game &quot;{HtmlEncode(game.Name)}&quot; is ready to start:",
                    gameUrl
                )
        ),
        ["notifyYourTurn"] = (
            game => $"It's your turn in '{game.Name}'",
            (user, game, gameUrl) =>
                BuildNotificationEmailHtml(
                    user.UserName,
                    $"It's your turn to play in &quot;{HtmlEncode(game.Name)}&quot;:",
                    gameUrl
                )
        ),
        ["notifyBribeForSupport"] = (
            game => $"You are attacked and now can call for support in '{game.Name}'",
            (user, game, gameUrl) =>
                BuildNotificationEmailHtml(
                    user.UserName,
                    $"You are attacked in the game &quot;{HtmlEncode(game.Name)}&quot; and now you can call for support or try to bribe your way there:",
                    gameUrl
                )
        ),
        ["notifyBattleResults"] = (
            game => $"Your battle is over in '{game.Name}'",
            (user, game, gameUrl) =>
                BuildNotificationEmailHtml(
                    user.UserName,
                    $"Your battle in &quot;{HtmlEncode(game.Name)}&quot; is over:",
                    gameUrl
                )
        ),
        ["notifyNewVote"] = (
            game => $"Your vote is needed in '{game.Name}'",
            (user, game, gameUrl) =>
                BuildNotificationEmailHtml(
                    user.UserName,
                    $"a new vote has been started in &quot;{HtmlEncode(game.Name)}&quot;:",
                    gameUrl
                )
        ),
        ["notifyGameEnded"] = (
            game => $"Game has ended -  {game.Name}",
            (user, game, gameUrl) =>
                BuildNotificationEmailHtml(
                    user.UserName,
                    $"The game &quot;{HtmlEncode(game.Name)}&quot; has ended:",
                    gameUrl
                )
        ),
    };

    internal static string BuildBodyHtml(
        string route,
        ApplicationUser user,
        Game game,
        string gameUrl
    ) => Templates[route].Body(user, game, gameUrl);

    internal static string BuildNotificationEmailHtml(
        string? userName,
        string explanationHtml,
        string gameUrl
    )
    {
        var encodedUserName = HtmlEncode(userName);
        var encodedGameUrl = HtmlEncode(gameUrl);

        return $"""
            <p>Hello {encodedUserName},</p>
            <p>{explanationHtml}</p>
            <p><a href="{encodedGameUrl}">{encodedGameUrl}</a></p>
            <p>Warmest regards,<br />Staff @ Swords and Ravens</p>
            """;
    }

    private static string HtmlEncode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

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
