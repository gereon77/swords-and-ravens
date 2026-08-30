using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Discord doesn't ship an official Microsoft.AspNetCore.Authentication.* package, so this is a
/// small custom handler built on the generic OAuth middleware, mirroring the shape of
/// Microsoft.AspNetCore.Authentication.Google. See MIGRATION_PLAN.md §5.
/// </summary>
public static class DiscordAuthenticationExtensions
{
    public const string DiscordAuthenticationDefaultScheme = "Discord";

    public static AuthenticationBuilder AddDiscord(this AuthenticationBuilder builder) =>
        builder.AddDiscord(DiscordAuthenticationDefaultScheme, _ => { });

    public static AuthenticationBuilder AddDiscord(
        this AuthenticationBuilder builder,
        Action<OAuthOptions> configureOptions
    ) => builder.AddDiscord(DiscordAuthenticationDefaultScheme, configureOptions);

    public static AuthenticationBuilder AddDiscord(
        this AuthenticationBuilder builder,
        string scheme,
        Action<OAuthOptions> configureOptions
    )
    {
        return builder.AddOAuth<OAuthOptions, DiscordOAuthHandler>(
            scheme,
            "Discord",
            options =>
            {
                options.AuthorizationEndpoint = "https://discord.com/api/oauth2/authorize";
                options.TokenEndpoint = "https://discord.com/api/oauth2/token";
                options.UserInformationEndpoint = "https://discord.com/api/users/@me";
                options.CallbackPath = "/signin-discord";
                options.Scope.Add("identify");
                options.Scope.Add("email");

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");

                configureOptions(options);
            }
        );
    }
}

public class DiscordOAuthHandler(
    IOptionsMonitor<OAuthOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder
) : OAuthHandler<OAuthOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        ClaimsIdentity identity,
        AuthenticationProperties properties,
        OAuthTokenResponse tokens
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokens.AccessToken
        );

        using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
        response.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(Context.RequestAborted)
        );
        var context = new OAuthCreatingTicketContext(
            new ClaimsPrincipal(identity),
            properties,
            Context,
            Scheme,
            Options,
            Backchannel,
            tokens,
            payload.RootElement
        );
        context.RunClaimActions();
        await Events.CreatingTicket(context);
        return new AuthenticationTicket(context.Principal!, context.Properties, Scheme.Name);
    }
}
