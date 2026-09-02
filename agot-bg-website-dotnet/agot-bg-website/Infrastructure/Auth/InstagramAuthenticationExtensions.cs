using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Instagram is the 3rd OIDC-ish provider requested in MIGRATION_PLAN.md §5.2. Meta's "Instagram
/// API with Instagram Login" flow only reliably returns `user_id` and `username` — email is often
/// unavailable, so the account-linking pipeline must be able to fall back to linking by provider
/// id alone (see MIGRATION_PLAN.md §5.2/§12 for the caveat).
/// </summary>
public static class InstagramAuthenticationExtensions
{
    public const string InstagramAuthenticationDefaultScheme = "Instagram";

    public static AuthenticationBuilder AddInstagram(this AuthenticationBuilder builder) =>
        builder.AddInstagram(InstagramAuthenticationDefaultScheme, _ => { });

    public static AuthenticationBuilder AddInstagram(
        this AuthenticationBuilder builder,
        Action<OAuthOptions> configureOptions
    ) => builder.AddInstagram(InstagramAuthenticationDefaultScheme, configureOptions);

    public static AuthenticationBuilder AddInstagram(
        this AuthenticationBuilder builder,
        string scheme,
        Action<OAuthOptions> configureOptions
    )
    {
        return builder.AddOAuth<OAuthOptions, InstagramOAuthHandler>(
            scheme,
            "Instagram",
            options =>
            {
                options.AuthorizationEndpoint = "https://www.instagram.com/oauth/authorize";
                options.TokenEndpoint = "https://api.instagram.com/oauth/access_token";
                options.UserInformationEndpoint = "https://graph.instagram.com/me";
                options.CallbackPath = "/signin-instagram";
                options.Scope.Add("user_profile");

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                // No email claim mapped: Instagram's Login product does not return one. Callers must
                // handle ClaimTypes.Email being absent — see the account-linking pipeline in Snr.Web.

                configureOptions(options);
            }
        );
    }
}

public class InstagramOAuthHandler(
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
        var endpoint =
            QueryHelpers_AddParameter(Options.UserInformationEndpoint, "fields", "id,username")
            + $"&access_token={Uri.EscapeDataString(tokens.AccessToken!)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
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

    private static string QueryHelpers_AddParameter(string url, string name, string value) =>
        url.Contains('?')
            ? $"{url}&{name}={Uri.EscapeDataString(value)}"
            : $"{url}?{name}={Uri.EscapeDataString(value)}";
}
