using System.Data.Common;
using System.Net.WebSockets;
using System.Security.Claims;
using agot_bg_website.Data;
using agot_bg_website.Infrastructure;
using agot_bg_website.Infrastructure.UsageMetrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

public class LastActivityMiddlewareTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CountsRejectedRequestsAndUsesAtomicPostgresUpdate(bool enabled)
    {
        var commands = new CaptureCommands();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .AddInterceptors(new SuppressConnections(), commands)
        );
        builder.Services.AddRateLimiter(options =>
            RequestRateLimiting.Configure(options, new() { StatisticsPerMinute = 1 })
        );
        await using var app = builder.Build();
        app.UseRouting();
        app.UseLastActivityTracking(enabled);
        app.UseRateLimiter();
        ((IApplicationBuilder)app).UseEndpoints(endpoints =>
            endpoints
                .MapGet("/statistics", () => "ok")
                .RequireRateLimiting(RequestRateLimiting.StatisticsPolicy)
        );
        var pipeline = ((IApplicationBuilder)app).Build();
        var id = Guid.NewGuid();

        for (var i = 0; i < 2; i++)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, id.ToString())],
                        "test"
                    )
                ),
            };
            context.Request.Path = "/statistics";
            context.Request.Method = "GET";
            context.Response.Body = new MemoryStream();
            await pipeline(context);
            Assert.Equal(i == 0 ? 200 : 429, context.Response.StatusCode);
        }

        Assert.Equal(2, commands.Sql.Count);
        foreach (var sql in commands.Sql)
        {
            Assert.Contains("\"LastActivity\"", sql);
            Assert.Contains("\"IsDeleted\"", sql);
            if (enabled)
            {
                Assert.Contains("\"UsageHour\"", sql);
                Assert.Contains("\"UsageRequests\"", sql);
                Assert.Contains("CASE", sql);
                Assert.Contains(" + 1", sql);
            }
            else
            {
                Assert.DoesNotContain("\"UsageRequests\"", sql);
            }
        }
    }

    [Fact]
    public async Task AnonymousRequestsDoNotUpdateAUser()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.UseLastActivityTracking(true);
        app.Run(context =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext { RequestServices = app.Services };
        await ((IApplicationBuilder)app).Build()(context);
        Assert.Equal(204, context.Response.StatusCode);
    }

    [Fact]
    public async Task MappedStaticAssetIsExcludedFromUsageAndActivity()
    {
        var commands = new CaptureCommands();
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(agot_bg_website.Pages.StatisticsModel)
                    .Assembly.GetName()
                    .Name,
                ContentRootPath = AppContext.BaseDirectory,
                WebRootPath = AppContext.BaseDirectory,
            }
        );
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .AddInterceptors(new SuppressConnections(), commands)
        );
        builder.Services.AddSingleton<UsageMetricsCollector>();
        await using var app = builder.Build();
        app.UseRouting();
        var matchedStaticAsset = false;
        app.Use(
            async (context, next) =>
            {
                var endpoint = context.GetEndpoint();
                matchedStaticAsset = StaticAssetEndpointMarker.IsStaticAsset(context);
                if (endpoint is not null)
                {
                    context.SetEndpoint(
                        new Endpoint(
                            ctx =>
                            {
                                ctx.Response.StatusCode = 200;
                                return Task.CompletedTask;
                            },
                            endpoint.Metadata,
                            endpoint.DisplayName
                        )
                    );
                }
                await next();
            }
        );
        app.UseMiddleware<UsageMetricsMiddleware>();
        app.UseLastActivityTracking(true);
        ((IApplicationBuilder)app).UseEndpoints(endpoints =>
            endpoints
                .MapStaticAssets()
                .Add(endpoint => endpoint.Metadata.Add(StaticAssetEndpointMarker.Instance))
        );

        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                    "test"
                )
            ),
        };
        context.Request.Path = "/raven.svg";
        context.Request.Method = "GET";
        await ((IApplicationBuilder)app).Build()(context);

        Assert.True(matchedStaticAsset);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Empty(commands.Sql);
        Assert.Empty(
            app.Services.GetRequiredService<UsageMetricsCollector>()
                .Snapshot(DateTime.UtcNow)
                .Values
        );
    }

    [Fact]
    public async Task WebSocketHandshakeUpdatesActivityButNotUsageCount()
    {
        var commands = new CaptureCommands();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .AddInterceptors(new SuppressConnections(), commands)
        );
        await using var app = builder.Build();
        app.UseLastActivityTracking(true);
        app.Run(context => Task.CompletedTask);
        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                    "test"
                )
            ),
        };
        context.Features.Set<IHttpWebSocketFeature>(new WebSocketRequest());
        await ((IApplicationBuilder)app).Build()(context);

        var sql = Assert.Single(commands.Sql);
        Assert.Contains("\"LastActivity\"", sql);
        Assert.DoesNotContain("\"UsageRequests\"", sql);
    }

    [Fact]
    public async Task CleanupClearsExpiredAccountCountersWithPostgresSql()
    {
        var commands = new CaptureCommands();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .AddInterceptors(new SuppressConnections(), commands)
        );
        await using var provider = services.BuildServiceProvider();
        var writer = new UsageMetricsWriter(
            new UsageMetricsCollector(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<UsageMetricsWriter>.Instance
        );

        await writer.DeleteExpiredAsync(TestContext.Current.CancellationToken);
        var index = commands.Sql.FindIndex(command => command.Contains("\"AspNetUsers\""));
        Assert.True(index >= 0);
        var sql = commands.Sql[index];
        Assert.Contains("\"UsageHour\" = NULL", sql);
        Assert.Contains("\"UsageRequests\" =", sql);
        Assert.Contains(0L, commands.Parameters[index]);
        Assert.Contains("\"UsageHour\" <", sql);
    }

    [Fact]
    public async Task ExceptionHandlerReexecutionDoesNotCountASecondRequest()
    {
        var commands = new CaptureCommands();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
                .AddInterceptors(new SuppressConnections(), commands)
        );
        builder.Services.AddSingleton<UsageMetricsCollector>();
        await using var app = builder.Build();
        app.UseExceptionHandler("/Error");
        app.UseRouting();
        app.UseMiddleware<UsageMetricsMiddleware>();
        app.UseLastActivityTracking(true);
        ((IApplicationBuilder)app).UseEndpoints(endpoints =>
        {
            endpoints
                .MapGet("/statistics", (HttpContext _) => throw new InvalidOperationException())
                .RequireRateLimiting(RequestRateLimiting.StatisticsPolicy);
            endpoints.MapGet("/Error", () => "error");
        });

        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                    "test"
                )
            ),
        };
        context.Request.Path = "/statistics";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        await ((IApplicationBuilder)app).Build()(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Single(commands.Sql);
        var value = Assert.Single(
            app.Services.GetRequiredService<UsageMetricsCollector>()
                .Snapshot(DateTime.UtcNow)
                .Values
        );
        Assert.Equal("statistics", value.Category);
        Assert.Equal("5xx", value.Outcome);
        Assert.Equal(1, value.Count);
    }

    private sealed class SuppressConnections : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class WebSocketRequest : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            throw new NotSupportedException();
    }

    private sealed class CaptureCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<object?[]> Parameters { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Sql.Add(command.CommandText);
            Parameters.Add(command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray());
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }
}
