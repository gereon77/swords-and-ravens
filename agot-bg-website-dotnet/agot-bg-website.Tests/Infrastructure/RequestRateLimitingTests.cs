using System.Net;
using System.Security.Claims;
using agot_bg_website.Api;
using agot_bg_website.Data;
using agot_bg_website.Infrastructure;
using agot_bg_website.Pages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

public class RequestRateLimitingTests
{
    [Fact]
    public async Task StatisticsAndUserPagesHaveSeparatePerUserLimits()
    {
        await using var app = CreateApp(new());
        var invoke = BuildPipeline(app);
        var id = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(200, (await Request(invoke, "/statistics", "alice")).Status);
            Assert.Equal(200, (await Request(invoke, $"/user/{id}", "alice")).Status);
        }

        var rejected = await Request(invoke, "/statistics", "alice");
        Assert.Equal(429, rejected.Status);
        Assert.NotEmpty(rejected.RetryAfter);
        Assert.StartsWith("text/html", rejected.ContentType);
        Assert.Contains("role=\"alert\"", rejected.Body);
        Assert.Contains("Too many requests", rejected.Body);
        Assert.Equal(429, (await Request(invoke, $"/user/{id}", "alice")).Status);
        Assert.Equal(200, (await Request(invoke, "/statistics", "bob")).Status);
        Assert.Equal(200, (await Request(invoke, $"/user/{id}", "bob")).Status);
        Assert.Equal(200, (await Request(invoke, "/unlimited", "alice")).Status);
    }

    [Fact]
    public async Task PublicGamesAreLimitedByIpIncludingMappedIpv4()
    {
        await using var app = CreateApp(new() { PublicGamesPerMinute = 2 });
        var invoke = BuildPipeline(app);
        var path = $"/api/public/game/{Guid.NewGuid()}";

        Assert.Equal(200, (await Request(invoke, path, ip: "192.0.2.1")).Status);
        Assert.Equal(200, (await Request(invoke, path, ip: "::ffff:192.0.2.1")).Status);
        var rejected = await Request(invoke, path, ip: "192.0.2.1");
        Assert.Equal(429, rejected.Status);
        Assert.StartsWith("application/json", rejected.ContentType);
        Assert.Contains("Too many requests", rejected.Body);
        Assert.Equal(200, (await Request(invoke, path, ip: "192.0.2.2")).Status);
    }

    [Fact]
    public async Task DisabledLimitsDoNotRejectRequests()
    {
        await using var app = CreateApp(new() { Enabled = false, PublicGamesPerMinute = 1 });
        var invoke = BuildPipeline(app);
        var id = Guid.NewGuid();

        for (var i = 0; i < 11; i++)
        {
            Assert.Equal(200, (await Request(invoke, "/statistics", "alice")).Status);
            Assert.Equal(200, (await Request(invoke, $"/user/{id}", "alice")).Status);
            Assert.Equal(200, (await Request(invoke, $"/api/public/game/{id}")).Status);
        }
    }

    [Fact]
    public async Task StatisticsConcurrencyIsSharedAndReleased()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = CreateApp(new() { StatisticsConcurrency = 1 });
        var invoke = BuildPipeline(
            app,
            async context =>
            {
                entered.TrySetResult();
                await release.Task;
                context.Response.StatusCode = 200;
            }
        );

        var first = Request(invoke, "/statistics", "alice");
        await entered.Task;
        try
        {
            Assert.Equal(429, (await Request(invoke, "/statistics", "bob")).Status);
        }
        finally
        {
            release.SetResult();
        }

        Assert.Equal(200, (await first).Status);
        Assert.Equal(200, (await Request(invoke, "/statistics", "bob")).Status);
        Assert.Equal(200, (await Request(invoke, "/unlimited", "bob")).Status);
    }

    private static WebApplication CreateApp(RequestRateLimitSettings settings)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(StatisticsModel).Assembly.FullName,
            }
        );
        builder.Services.AddRazorPages().AddApplicationPart(typeof(StatisticsModel).Assembly);
        builder.Services.AddScoped<ApplicationDbContext>(_ =>
            throw new InvalidOperationException()
        );
        builder.Services.AddRateLimiter(options =>
            RequestRateLimiting.Configure(options, settings)
        );
        return builder.Build();
    }

    private static RequestDelegate BuildPipeline(
        WebApplication app,
        RequestDelegate? statisticsHandler = null
    )
    {
        app.UseRouting();
        app.Use(
            async (context, next) =>
            {
                var endpoint = context.GetEndpoint();
                if (endpoint is not null)
                {
                    RequestDelegate handler =
                        statisticsHandler is not null && context.Request.Path.Equals("/statistics")
                            ? statisticsHandler
                            : ctx =>
                            {
                                ctx.Response.StatusCode = 200;
                                return Task.CompletedTask;
                            };
                    context.SetEndpoint(
                        new Endpoint(handler, endpoint.Metadata, endpoint.DisplayName)
                    );
                }

                await next();
            }
        );
        app.UseRateLimiter();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
            endpoints.MapPublicApi();
            endpoints.MapGet("/unlimited", () => "ok");
        });
        return ((IApplicationBuilder)app).Build();
    }

    private static async Task<(
        int Status,
        string RetryAfter,
        string ContentType,
        string Body
    )> Request(RequestDelegate invoke, string path, string? user = null, string ip = "192.0.2.1")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();
        if (user is not null)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test")
            );
        }

        await invoke(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (
            context.Response.StatusCode,
            context.Response.Headers.RetryAfter.ToString(),
            context.Response.ContentType ?? "",
            body
        );
    }
}
