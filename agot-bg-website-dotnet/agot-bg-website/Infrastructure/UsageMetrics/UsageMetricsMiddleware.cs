using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;

namespace agot_bg_website.Infrastructure.UsageMetrics;

internal sealed class UsageMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UsageMetricsCollector collector)
    {
        if (
            context.Features.Get<IExceptionHandlerPathFeature>() is not null
            || context.WebSockets.IsWebSocketRequest
            || StaticAssetEndpointMarker.IsStaticAsset(context)
            || IsStatic(context.Request.Path)
        )
        {
            await next(context);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            collector.Record(
                Category(context),
                Outcome(failed ? 500 : context.Response.StatusCode),
                Stopwatch.GetElapsedTime(start).TotalSeconds
            );
        }
    }

    private static bool IsStatic(PathString path) =>
        path.StartsWithSegments("/static")
        || path.StartsWithSegments("/static_game")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/js")
        || path.StartsWithSegments("/lib")
        || path.StartsWithSegments("/images");

    private static string Category(HttpContext context) =>
        context
            .GetEndpoint()
            ?.Metadata.GetMetadata<EnableRateLimitingAttribute>()
            ?.PolicyName switch
        {
            RequestRateLimiting.StatisticsPolicy => "statistics",
            RequestRateLimiting.UserPagePolicy => "profiles",
            RequestRateLimiting.PublicGamesPolicy => "public-api",
            _ when context.Request.Path.StartsWithSegments("/api/public") => "public-api",
            _ when context.Request.Path.StartsWithSegments("/api") => "other-api",
            _ => "other",
        };

    private static string Outcome(int status) =>
        status switch
        {
            429 => "429",
            >= 200 and < 300 => "2xx",
            >= 300 and < 400 => "3xx",
            >= 400 and < 500 => "4xx",
            >= 500 and < 600 => "5xx",
            _ => "other",
        };
}
