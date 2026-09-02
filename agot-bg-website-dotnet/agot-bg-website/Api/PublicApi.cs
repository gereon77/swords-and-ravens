using System.Text.Json.Nodes;
using agot_bg_website.Data;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// GET /api/public/game/{id} — anonymous, unauthenticated access (matches Django's real behavior:
/// <c>@permission_classes([])</c> i.e. AllowAny, despite api/PUBLIC_API.md's stale "Authentication
/// Required: Yes" claim — the Django docstring itself says "Public API endpoint for anonymous
/// access"). Deliberately has no session-cookie/Basic-Auth requirement at all so third-party sites
/// embedding live game state can call it directly (plain server-side GET, no cookies to forward,
/// no CORS preflight/credentials dance). Strips server-only fields and renames turn->round, exactly
/// like Django's public serializer — see MIGRATION_PLAN.md §6.
/// </summary>
public static class PublicApi
{
    private static readonly string[] FieldsToStrip =
    [
        "replacerIds",
        "oldPlayerIds",
        "waitingForIds",
        "publicChatRoomId",
        "timeoutPlayerIds",
    ];

    public static RouteGroupBuilder MapPublicApi(this IEndpointRouteBuilder app)
    {
        // "public" group name drives which endpoints Program.cs's AddOpenApi document includes
        // (see ShouldInclude there) — this is the only Minimal API group meant to be documented
        // at /api/docs. PlayApi returns HTML (not JSON), and UsersApi/GamesApi/RoomsApi/
        // NotificationsApi are the private, port-restricted game-server contract, so none of those
        // carry this group name and are excluded from the generated document.
        //
        // No .RequireAuthorization() here (intentionally, see class doc comment above) —
        // AllowAnonymous is Minimal API's default, so nothing further is needed; there is also no
        // fallback authorization policy configured in Program.cs that would otherwise apply.
        var group = app.MapGroup("/api/public").WithGroupName("public");

        group
            .MapGet(
                "/game/{id:guid}",
                async (Guid id, ApplicationDbContext db) =>
                {
                    var viewOfGame = await db
                        .Games.Where(g => g.Id == id)
                        .Select(g => g.ViewOfGame)
                        .FirstOrDefaultAsync();
                    if (viewOfGame is null)
                    {
                        return Results.NotFound();
                    }

                    var node = JsonNode.Parse(viewOfGame.RootElement.GetRawText())!.AsObject();
                    foreach (var field in FieldsToStrip)
                    {
                        node.Remove(field);
                    }

                    if (node.TryGetPropertyValue("turn", out var turnValue))
                    {
                        node.Remove("turn");
                        node["round"] = turnValue?.DeepClone();
                    }

                    return Results.Text(node.ToJsonString(), "application/json");
                }
            )
            .WithName("GetPublicGame")
            .WithSummary("Get a game's public view")
            .WithDescription(
                "Returns the same denormalized view of a game shown in the browser client, with "
                    + "server-only fields (replacer/old-player/waiting-for ids, the internal chat room id, "
                    + "timeout tracking) stripped and \"turn\" renamed to \"round\". Anonymous/unauthenticated "
                    + "— no session cookie or credentials required; see api/PUBLIC_API.md."
            )
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }
}
