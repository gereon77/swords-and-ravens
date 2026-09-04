using System.Security.Claims;
using agot_bg_website.Data;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Infrastructure;

/// <summary>
/// Refreshes <see cref="Domain.ApplicationUser.LastActivity"/> on every request made by an
/// authenticated user, mirroring Django's <c>update_last_activity</c> middleware
/// (agotboardgame_main/middlewares.py) that this replaces. Feeds the "Last activity" shown on
/// User.cshtml and the "waiting for" / inactive-player detection in GameListQueryService.
/// </summary>
public static class LastActivityMiddlewareExtensions
{
    /// <summary>
    /// Must be registered after <c>UseAuthentication()</c> (so <c>HttpContext.User</c> is
    /// populated) — placement relative to <c>UseAuthorization()</c> doesn't matter since this
    /// never rejects a request, it just best-effort records activity alongside whatever the rest
    /// of the pipeline decides to do with it.
    /// </summary>
    public static IApplicationBuilder UseLastActivityTracking(this IApplicationBuilder app)
    {
        return app.Use(
            async (context, next) =>
            {
                if (
                    context.User.Identity?.IsAuthenticated == true
                    && Guid.TryParse(
                        context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                        out var userId
                    )
                )
                {
                    // A single bulk UPDATE instead of loading the full ApplicationUser + SaveChanges -
                    // this runs on every single authenticated request, so avoiding the round-trip to
                    // fetch a row we're about to overwrite (and the risk of clobbering a concurrent
                    // write to some other field on the same user) matters here.
                    var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
                    await db
                        .Users.Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(s =>
                            s.SetProperty(u => u.LastActivity, DateTimeOffset.UtcNow)
                        );
                }

                await next(context);
            }
        );
    }
}
