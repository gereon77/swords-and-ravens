using System.Text.Json;
using System.Text.Json.Nodes;
using agot_bg_website.Data;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Api;

/// <summary>
/// GET /api/public/game/{id} — session-cookie-authenticated (not MasterApi Basic Auth), consumed
/// by front-end tooling per api/PUBLIC_API.md. Strips server-only fields and renames turn->round,
/// exactly like Django's public serializer — see MIGRATION_PLAN.md §6.
/// </summary>
public static class PublicApi
{
    private static readonly string[] FieldsToStrip =
    [
        "replacerIds", "oldPlayerIds", "waitingForIds", "publicChatRoomId", "timeoutPlayerIds"
    ];

    public static RouteGroupBuilder MapPublicApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public").RequireAuthorization();

        group.MapGet("/game/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var viewOfGame = await db.Games.Where(g => g.Id == id).Select(g => g.ViewOfGame).FirstOrDefaultAsync();
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
        });

        return group;
    }
}
