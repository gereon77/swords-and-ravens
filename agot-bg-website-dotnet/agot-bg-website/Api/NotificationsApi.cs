using agot_bg_website.Data;
using agot_bg_website.Domain;

namespace agot_bg_website.Api;

/// <summary>
/// The handful of notification/misc endpoints the game server calls (5 "notify" endpoints in
/// Django + addPbemResponseTime). Only addPbemResponseTime is fully implemented here; the notify
/// endpoints are stubbed with the correct route/auth shape as a starting point for wiring up
/// actual email sending — see MIGRATION_PLAN.md §6.
/// </summary>
public static class NotificationsApi
{
    public static RouteGroupBuilder MapNotificationsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization(Infrastructure.Auth.MasterApiAuthenticationHandler.SchemeName);

        group.MapPost("/addPbemResponseTime/{userId:guid}/{responseTime:int}",
            async (Guid userId, int responseTime, ApplicationDbContext db) =>
            {
                db.PbemResponseTimes.Add(new PbemResponseTime
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ResponseTime = responseTime
                });
                await db.SaveChangesAsync();
                return Results.NoContent();
            });

        foreach (var route in new[]
        {
            "notifyReadyToStart", "notifyWaitingForVotes", "notifyGameEnded", "notifyNewPbemResponse", "notifyAdmin"
        })
        {
            group.MapPost($"/{route}/{{gameId:guid}}", (Guid gameId, ILogger<Program> logger) =>
            {
                // TODO: wire up real email sending here, mirroring the Django templates this
                // replaces. Logged instead of throwing NotImplementedException so local dev /
                // integration smoke tests against the game server don't hard-fail on this yet.
                logger.LogInformation("{Route} called for game {GameId} (not yet implemented)", route, gameId);
                return Results.NoContent();
            });
        }

        return group;
    }
}
