using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace agot_bg_website.Infrastructure;

public sealed class RequestRateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int StatisticsPerMinute { get; set; } = 10;
    public int UserPagesPerMinute { get; set; } = 10;
    public int PublicGamesPerMinute { get; set; } = 60;
    public int StatisticsConcurrency { get; set; } = 4;
}

public static class RequestRateLimiting
{
    public const string StatisticsPolicy = "statistics";
    public const string UserPagePolicy = "user-page";
    public const string PublicGamesPolicy = "public-games";

    public static void Configure(RateLimiterOptions options, RequestRateLimitSettings settings)
    {
        if (
            settings.StatisticsPerMinute <= 0
            || settings.UserPagesPerMinute <= 0
            || settings.PublicGamesPerMinute <= 0
            || settings.StatisticsConcurrency <= 0
        )
        {
            throw new InvalidOperationException("Request rate limits must be positive.");
        }

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(
                        retryAfter.TotalSeconds
                    )
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var response = context.HttpContext.Response;
            response.Headers.CacheControl = "no-store";
            if (GetPolicyName(context.HttpContext) == PublicGamesPolicy)
            {
                response.ContentType = "application/json; charset=utf-8";
                await response.WriteAsync(
                    """{"error":"Too many requests. Please try again later."}""",
                    cancellationToken
                );
            }
            else
            {
                response.ContentType = "text/html; charset=utf-8";
                await response.WriteAsync(
                    """
                    <!DOCTYPE html>
                    <html lang="en" data-theme="swordsandravens">
                    <head>
                        <meta charset="utf-8">
                        <title>Too many requests - Swords and Ravens</title>
                        <link rel="stylesheet" href="/css/app.css">
                    </head>
                    <body>
                        <main class="max-w-3xl mx-auto p-4">
                            <div class="alert alert-warning" role="alert">
                                Too many requests. Please wait a moment before trying again.
                            </div>
                            <p class="mt-4">Use your browser's Back button to return, or <a class="link" href="/">go to the home page</a>.</p>
                        </main>
                    </body>
                    </html>
                    """,
                    cancellationToken
                );
            }
        };

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!settings.Enabled || GetPolicyName(context) != StatisticsPolicy)
            {
                return RateLimitPartition.GetNoLimiter("unlimited");
            }

            return RateLimitPartition.GetConcurrencyLimiter(
                StatisticsPolicy,
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = settings.StatisticsConcurrency,
                    QueueLimit = 0,
                }
            );
        });

        options.AddPolicy(
            StatisticsPolicy,
            context => PerMinute(settings, UserKey(context), settings.StatisticsPerMinute)
        );
        options.AddPolicy(
            UserPagePolicy,
            context => PerMinute(settings, UserKey(context), settings.UserPagesPerMinute)
        );
        options.AddPolicy(
            PublicGamesPolicy,
            context => PerMinute(settings, IpKey(context), settings.PublicGamesPerMinute)
        );
    }

    private static RateLimitPartition<string> PerMinute(
        RequestRateLimitSettings settings,
        string key,
        int permitLimit
    ) =>
        settings.Enabled
            ? RateLimitPartition.GetFixedWindowLimiter(
                key,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
            : RateLimitPartition.GetNoLimiter("disabled");

    private static string UserKey(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return context.User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(userId)
            ? $"user:{userId}"
            : IpKey(context);
    }

    private static string? GetPolicyName(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static string IpKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip?.IsIPv4MappedToIPv6 == true)
        {
            ip = ip.MapToIPv4();
        }

        return $"ip:{ip?.ToString() ?? "unknown"}";
    }
}
