using System.Security.Cryptography;

namespace agot_bg_website.Infrastructure;

/// <summary>
/// Adds a per-request CSP nonce to <see cref="HttpContext.Items"/> and emits a
/// <c>Content-Security-Policy-Report-Only</c> header on every response. Report-only so it never
/// blocks anything yet - it just lets browsers log violations, which the <c>/csp-report</c>
/// endpoint (see Program.cs) records for review. This is intentionally the first step of a
/// two-phase rollout: only switch to an enforcing <c>Content-Security-Policy</c> header once a
/// real observation period (covering /play, the chat widget, Identity/Turnstile registration, and
/// the Admin/CoreAdmin/Scalar areas) confirms the policy below doesn't need further loosening.
///
/// Inline &lt;script&gt; blocks (_Layout.cshtml, _ChatWidget.cshtml, _CookieConsentPartial.cshtml,
/// _Pager.cshtml, Games.cshtml, MyGames.cshtml) render <c>nonce="@Context.GetCspNonce()"</c> so
/// they're allowed under the nonce below without resorting to 'unsafe-inline' for script
/// elements - the actual protection this policy buys against an attacker injecting a brand new
/// &lt;script&gt; tag, by far the most common real-world XSS payload shape.
///
/// Inline event-handler attributes (onclick=/onsubmit=/onchange=, e.g. the confirm() dialogs in
/// _GamesTable.cshtml and the Admin area) and inline style="..." attributes (dynamic per-provider
/// button colors in Login.cshtml/Register.cshtml/ExternalLogins.cshtml) are deliberately allowed
/// via 'unsafe-inline' on script-src-attr/style-src-attr instead of being retrofitted with
/// nonces/hashes: CSP nonces don't apply to attributes at all, and hashing dozens of dynamic
/// per-page attribute values isn't practical. This is a common, intentional trade-off.
/// </summary>
public static class ContentSecurityPolicyMiddlewareExtensions
{
    private const string CspNonceItemsKey = "csp-nonce";

    // Must match ASSET_PATH in .github/workflows/deploy.yml - the DigitalOcean Spaces CDN bucket
    // the game client's webpack build embeds its script/media/font URLs under. Update both if the
    // bucket name or region slug ever changes.
    private const string GameClientCdnOrigin =
        "https://swords-and-ravens-spaces.fra1.cdn.digitaloceanspaces.com";

    /// <summary>
    /// Reads the CSP nonce generated for the current request by
    /// <see cref="UseContentSecurityPolicy"/>. Razor views call this
    /// (<c>nonce="@Context.GetCspNonce()"</c>) on every inline &lt;script&gt; block so it's
    /// allowed under the nonce-based policy below.
    /// </summary>
    public static string GetCspNonce(this HttpContext context) =>
        (string)context.Items[CspNonceItemsKey]!;

    /// <summary>
    /// Must run before routing/Razor Pages execute (so the nonce exists in
    /// <see cref="HttpContext.Items"/> by the time a page renders) - registered early in
    /// Program.cs, right after <c>UseForwardedHeaders</c>.
    /// </summary>
    public static IApplicationBuilder UseContentSecurityPolicy(this IApplicationBuilder app)
    {
        return app.Use(
            async (context, next) =>
            {
                var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                context.Items[CspNonceItemsKey] = nonce;

                context.Response.OnStarting(() =>
                {
                    // Scalar's bundled API reference UI (/api/docs, see Program.cs's
                    // MapScalarApiReference) is a third-party dev tool we have no intention of
                    // CSP-hardening - exempted so it doesn't drown real violations in noise.
                    if (
                        !context.Request.Path.StartsWithSegments(
                            "/api/docs",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        var config = context.RequestServices.GetRequiredService<IConfiguration>();
                        context.Response.Headers["Content-Security-Policy-Report-Only"] =
                            BuildPolicy(config, nonce);
                    }
                    return Task.CompletedTask;
                });

                await next(context);
            }
        );
    }

    private static string BuildPolicy(IConfiguration config, string nonce)
    {
        // Mirrors GameClient.ts's own "localhost -> ws://localhost:5000, else ->
        // wss://play.<host>" branch (agot-bg-game-server/src/client/GameClient.ts), so this stays
        // correct for local dev, Staging (winordie.net) and Production (swordsandravens.net)
        // without hardcoding a hostname.
        var publicSiteHost = new Uri(config["PublicSiteUrl"] ?? "http://localhost:8000").Host;
        var gameWebSocketOrigin =
            publicSiteHost == "localhost" ? "ws://localhost:5000" : $"wss://play.{publicSiteHost}";

        string[] directives =
        [
            "default-src 'self'",
            $"script-src-elem 'self' 'nonce-{nonce}' https://challenges.cloudflare.com {GameClientCdnOrigin}",
            "script-src-attr 'unsafe-inline'",
            "style-src 'self'",
            "style-src-attr 'unsafe-inline'",
            "font-src 'self'",
            $"img-src 'self' data: {GameClientCdnOrigin}",
            $"media-src {GameClientCdnOrigin}",
            $"connect-src 'self' {gameWebSocketOrigin}",
            "frame-src https://challenges.cloudflare.com",
            "form-action 'self'",
            "frame-ancestors 'self'",
            "base-uri 'self'",
            "object-src 'none'",
            "report-uri /csp-report",
        ];
        return string.Join("; ", directives);
    }
}
