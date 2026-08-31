using agot_bg_website.Api;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Infrastructure.Chat;
using agot_bg_website.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// GDPR: require explicit cookie consent (via _CookieConsentPartial.cshtml) before any
// non-essential cookie is written. Identity's own auth cookies are marked "Essential" by the
// framework, so signing in/out keeps working even before a visitor accepts the banner.
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = _ => true;
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
});

// Locally there is usually no mail sender configured and no OAuth app secrets, so:
//  - email confirmation is only required outside Development (so local individual/password
//    accounts can log in immediately after registering), and
//  - each external OAuth provider below is only wired up when its ClientId/ClientSecret are
//    actually configured (via appsettings/user-secrets/env vars), so local debugging can run
//    with individual (username/password) accounts only, with no OAuth app registrations needed.
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.SignIn.RequireConfirmedAccount = !builder.Environment.IsDevelopment();
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

// GDPR: Identity's own sign-in cookie is essential for the site to function (you can't be signed
// in without it), so it's exempt from the cookie-consent banner above — see
// https://learn.microsoft.com/aspnet/core/security/gdpr.
builder.Services.ConfigureApplicationCookie(options => options.Cookie.IsEssential = true);
builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
    IdentityConstants.ExternalScheme, options => options.Cookie.IsEssential = true);

builder.Services.AddScoped<AccountLinkingService>();

// Chat (MIGRATION_PLAN.md §7) — raw ASP.NET Core WebSockets + Redis pub/sub, replacing Django
// Channels, so ChatClient.ts/games_chat.html don't need any changes.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' not found.");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ChatConnectionManager>();
builder.Services.AddSingleton<ChatPresenceService>();
builder.Services.AddSingleton<ChatBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChatBroadcaster>());

// Real SMTP email sending — used by both Identity's own emails (password reset, email
// confirmation) and NotificationsApi's game-notification endpoints, see MIGRATION_PLAN.md §6/§9.1.
// Only overrides Identity's built-in no-op IEmailSender when Email:Host is actually configured,
// same "only wire it up when configured" pattern as the OAuth providers below, so local dev
// doesn't need a working mail server.
if (!string.IsNullOrEmpty(builder.Configuration["Email:Host"]))
{
    builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, agot_bg_website.Services.SmtpEmailSender>();
}

builder.Services.AddRazorPages();

// External logins: Google/Discord/Instagram — see MIGRATION_PLAN.md §5. Plus a MasterApi Basic
// Auth scheme for the game server's service-to-service calls, see MIGRATION_PLAN.md §6.
var authenticationBuilder = builder.Services.AddAuthentication();

bool IsConfigured(string clientIdKey, string clientSecretKey) =>
    !string.IsNullOrEmpty(builder.Configuration[clientIdKey]) &&
    !string.IsNullOrEmpty(builder.Configuration[clientSecretKey]);

if (IsConfigured("Authentication:Google:ClientId", "Authentication:Google:ClientSecret"))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
    });
}

if (IsConfigured("Authentication:Discord:ClientId", "Authentication:Discord:ClientSecret"))
{
    authenticationBuilder.AddDiscord(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Discord:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Discord:ClientSecret"]!;
    });
}

if (IsConfigured("Authentication:Instagram:ClientId", "Authentication:Instagram:ClientSecret"))
{
    authenticationBuilder.AddInstagram(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Instagram:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Instagram:ClientSecret"]!;
    });
}

authenticationBuilder.AddScheme<MasterApiAuthenticationOptions, MasterApiAuthenticationHandler>(
    MasterApiAuthenticationHandler.SchemeName,
    options =>
    {
        options.Username = builder.Configuration["GameServer:MasterApiUsername"] ?? string.Empty;
        options.Password = builder.Configuration["GameServer:MasterApiPassword"] ?? string.Empty;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(MasterApiAuthenticationHandler.SchemeName, policy =>
        policy.AddAuthenticationSchemes(MasterApiAuthenticationHandler.SchemeName).RequireAuthenticatedUser());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
}

app.UseHttpsRedirection();
app.UseCookiePolicy();
app.UseWebSockets();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Minimal API groups — the REST contract the game server speaks, see MIGRATION_PLAN.md §6.
app.MapUsersApi();
app.MapGamesApi();
app.MapRoomsApi();
app.MapPublicApi();
app.MapNotificationsApi();
app.MapPlayApi();
app.MapChatWebSocket();

using (var scope = app.Services.CreateScope())
{
    await agot_bg_website.Infrastructure.Auth.RoleSeeder.SeedAsync(scope.ServiceProvider);
    await RoomSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();

// Needed so WebApplicationFactory<Program> works from the test project.
public partial class Program;

