namespace agot_bg_website.Infrastructure;

/// <summary>
/// Endpoint metadata marking a route group as only reachable via a specific local (server-side)
/// TCP port. See <see cref="EndpointRoutingExtensions.RequireLocalPort"/> and
/// <see cref="EndpointRoutingExtensions.UseLocalPortRestriction"/>.
/// </summary>
public sealed class RequireLocalPortMetadata(int port)
{
    public int Port { get; } = port;
}

public static class EndpointRoutingExtensions
{
    /// <summary>
    /// Marks a Minimal API route group as reachable only via connections physically accepted on
    /// the given local TCP port. Used to keep the private, Basic-Auth-only game-server REST API
    /// (Api/UsersApi.cs, GamesApi.cs, RoomsApi.cs, NotificationsApi.cs) unreachable via the
    /// public-facing Kestrel endpoint even if a caller sends a Host header that happens to match
    /// the internal one — see appsettings.json's Kestrel:Endpoints section and the comment in
    /// Program.cs for the full reasoning.
    ///
    /// Deliberately implemented as endpoint metadata enforced by
    /// <see cref="UseLocalPortRestriction"/> — a plain middleware registered before
    /// UseAuthentication/UseAuthorization — rather than as an endpoint filter
    /// (<c>AddEndpointFilter</c>). Endpoint filters run *after* the authorization middleware, so a
    /// request to the wrong port would still reach the point of getting a 401 Basic Auth
    /// challenge before the filter ever ran, leaking the fact that the endpoint exists (and its
    /// auth prompt) on the public port. Running the port check as early middleware instead means
    /// the wrong-port request never reaches authentication/authorization or the endpoint at all —
    /// it gets a plain 404, indistinguishable from a route that doesn't exist.
    ///
    /// Also deliberately checked via
    /// <see cref="ConnectionInfo.LocalPort"/> (the actual socket the
    /// connection was accepted on) rather than <c>RequireHost</c>/<c>Host</c> header matching: the
    /// <c>Host</c> header is supplied by the client and can be spoofed, so a
    /// <c>RequireHost("*:8001")</c>-style check could be bypassed by a request that physically
    /// connects on the public port but sends <c>Host: internal:8001</c>. <c>Connection.LocalPort</c>
    /// instead reflects which literal Kestrel endpoint accepted the TCP connection and cannot be
    /// influenced by request headers.
    /// </summary>
    public static RouteGroupBuilder RequireLocalPort(this RouteGroupBuilder group, int port)
    {
        group.WithMetadata(new RequireLocalPortMetadata(port));
        return group;
    }

    /// <summary>
    /// Enforces <see cref="RequireLocalPort"/> restrictions. Must be registered after
    /// <c>UseRouting()</c> (so <c>HttpContext.GetEndpoint()</c> is populated) but before
    /// <c>UseAuthentication()</c>/<c>UseAuthorization()</c>, so a request to the wrong port is
    /// rejected before it ever reaches the Basic Auth challenge.
    /// </summary>
    public static IApplicationBuilder UseLocalPortRestriction(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var metadata = context.GetEndpoint()?.Metadata.GetMetadata<RequireLocalPortMetadata>();
            if (metadata is not null && context.Connection.LocalPort != metadata.Port)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }
}
