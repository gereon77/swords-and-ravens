using agot_bg_website.Infrastructure;
using agot_bg_website.Infrastructure.UsageMetrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

public class UsageMetricsTests
{
    [Fact]
    public async Task MiddlewareCountsRealRateLimiterRejections()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<UsageMetricsCollector>();
        builder.Services.AddRateLimiter(options =>
            RequestRateLimiting.Configure(options, new() { StatisticsPerMinute = 1 })
        );
        await using var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<UsageMetricsMiddleware>();
        app.UseRateLimiter();
        app.UseEndpoints(endpoints =>
            endpoints
                .MapGet("/statistics", () => "ok")
                .RequireRateLimiting(RequestRateLimiting.StatisticsPolicy)
        );
        var pipeline = ((IApplicationBuilder)app).Build();
        for (var i = 0; i < 2; i++)
        {
            var context = new DefaultHttpContext { RequestServices = app.Services };
            context.Request.Path = "/statistics";
            context.Request.Method = "GET";
            context.Response.Body = new MemoryStream();
            await pipeline(context);
            Assert.Equal(i == 0 ? 200 : 429, context.Response.StatusCode);
        }

        var values = app
            .Services.GetRequiredService<UsageMetricsCollector>()
            .Snapshot(DateTime.UtcNow)
            .Values;
        Assert.Contains(
            values,
            v => v.Category == "statistics" && v.Outcome == "2xx" && v.Count == 1
        );
        Assert.Contains(
            values,
            v => v.Category == "statistics" && v.Outcome == "429" && v.Count == 1
        );
    }

    [Fact]
    public async Task RecordsOnlyFixedCategoriesAndStatusesWithoutRequestDetails()
    {
        var collector = new UsageMetricsCollector();
        var middleware = new UsageMetricsMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 429;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/user/secret-user-id";
        context.Request.QueryString = new("?token=secret");
        context.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(
                    new EnableRateLimitingAttribute(RequestRateLimiting.UserPagePolicy)
                ),
                "profile"
            )
        );

        await middleware.InvokeAsync(context, collector);
        var batch = collector.Snapshot(DateTime.UtcNow);
        var value = Assert.Single(batch.Values);
        Assert.Equal("profiles", value.Category);
        Assert.Equal("429", value.Outcome);
        Assert.Equal(1, value.Count);
        Assert.True(value.Seconds >= 0);
        Assert.Empty(collector.Snapshot(DateTime.UtcNow).Values);
    }

    [Fact]
    public async Task ExceptionsAreRecordedAndPropagatedAndStaticRequestsAreExcluded()
    {
        var collector = new UsageMetricsCollector();
        var middleware = new UsageMetricsMiddleware(_ =>
            throw new InvalidOperationException("test")
        );
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/public/game/secret";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(context, collector)
        );
        context.Request.Path = "/css/app.css";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(context, collector)
        );

        var value = Assert.Single(collector.Snapshot(DateTime.UtcNow).Values);
        Assert.Equal("public-api", value.Category);
        Assert.Equal("5xx", value.Outcome);
        Assert.Equal(1, value.Count);
    }

    [Fact]
    public async Task ConcurrentRecordsAreNotLostAcrossSnapshots()
    {
        var collector = new UsageMetricsCollector();
        await Task.WhenAll(
            Enumerable
                .Range(0, 1000)
                .Select(_ => Task.Run(() => collector.Record("statistics", "2xx", 0.1)))
        );
        var result = Assert.Single(collector.Snapshot(DateTime.UtcNow).Values);
        Assert.Equal(1000, result.Count);
        Assert.Equal(100, result.Seconds, 5);
        Assert.Empty(collector.Snapshot(DateTime.UtcNow).Values);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            collector.Record("unbounded-label", "2xx", 1)
        );
    }
}
