using System.Security.Claims;
using agot_bg_website.Data;
using Microsoft.AspNetCore.Diagnostics;
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
    /// populated) and authorization, but before rate limiting so 429 responses are counted.
    /// </summary>
    public static IApplicationBuilder UseLastActivityTracking(
        this IApplicationBuilder app,
        bool usageMetricsEnabled
    )
    {
        return app.Use(
            async (context, next) =>
            {
                if (
                    context.Features.Get<IExceptionHandlerPathFeature>() is not null
                    || StaticAssetEndpointMarker.IsStaticAsset(context)
                )
                {
                    await next(context);
                    return;
                }

                if (
                    context.User.Identity?.IsAuthenticated == true
                    && Guid.TryParse(
                        context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                        out var userId
                    )
                )
                {
                    var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var now = DateTimeOffset.UtcNow;
                    var users = db.Users.Where(u => u.Id == userId && !u.IsDeleted);
                    if (usageMetricsEnabled && !context.WebSockets.IsWebSocketRequest)
                    {
                        var hour = new DateTimeOffset(
                            now.Year,
                            now.Month,
                            now.Day,
                            now.Hour,
                            0,
                            0,
                            TimeSpan.Zero
                        );
                        // A request delayed across an hour boundary must not reset a newer bucket.
                        await users.ExecuteUpdateAsync(s =>
                            s.SetProperty(
                                    u => u.LastActivity,
                                    u => u.LastActivity > now ? u.LastActivity : now
                                )
                                .SetProperty(
                                    u => u.UsageRequests,
                                    u =>
                                        u.UsageHour != null && u.UsageHour >= hour
                                            ? u.UsageRequests + 1
                                            : 1
                                )
                                .SetProperty(
                                    u => u.UsageHour,
                                    u =>
                                        u.UsageHour != null && u.UsageHour >= hour
                                            ? u.UsageHour
                                            : hour
                                )
                        );
                    }
                    else
                    {
                        await users.ExecuteUpdateAsync(s =>
                            s.SetProperty(
                                u => u.LastActivity,
                                u => u.LastActivity > now ? u.LastActivity : now
                            )
                        );
                    }
                }

                await next(context);
            }
        );
    }
}
