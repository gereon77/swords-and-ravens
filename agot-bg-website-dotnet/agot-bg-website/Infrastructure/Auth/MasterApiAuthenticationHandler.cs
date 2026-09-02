using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace agot_bg_website.Infrastructure.Auth;

public class MasterApiAuthenticationOptions : AuthenticationSchemeOptions
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Replicates DRF's BasicAuthentication + IsAdminUser for the handful of endpoints the game
/// server calls (see MIGRATION_PLAN.md §6). Credentials come from MASTER_API_USERNAME /
/// MASTER_API_PASSWORD-equivalent config, matching what LiveWebsiteClient.ts already sends —
/// no changes needed on the Node side.
/// </summary>
public class MasterApiAuthenticationHandler(
    IOptionsMonitor<MasterApiAuthenticationOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<MasterApiAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "MasterApi";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var header = authHeader.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic auth header"));
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic auth header"));
        }

        var username = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        if (username != Options.Username || password != Options.Password)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid master API credentials"));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
