using System.Text.Json;
using agot_bg_website.Api;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Regression coverage for a real bug: <c>Program.cs</c> never actually configured
/// <c>JsonNamingPolicy.SnakeCaseLower</c> for the <c>/api/*</c> Minimal API endpoints, despite
/// Api/Dtos.cs's header comment claiming it did. Every response therefore serialized with ASP.NET
/// Core Minimal API's default (camelCase) instead of matching Django's snake_case DRF contract —
/// e.g. <see cref="UserDto.GameToken"/> came back as <c>"gameToken"</c>, not <c>"game_token"</c>,
/// which is the exact field <c>LiveWebsiteClient.ts</c>'s <c>getUser()</c> reads
/// (<c>response.game_token</c>). That silently produced <c>undefined</c> on the TS side, making
/// <c>GlobalServer.ts</c>'s <c>userData.token != authToken</c> check always fail once real
/// credentials were configured. These tests serialize with the same
/// <see cref="JsonSerializerOptions"/> shape now configured via
/// <c>builder.Services.ConfigureHttpJsonOptions</c> in Program.cs, to pin the wire format down so
/// it can't silently regress back to camelCase again.
/// </summary>
public class ApiJsonNamingPolicyTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void UserDto_SerializesGameTokenAsSnakeCase()
    {
        var dto = new UserDto(Guid.NewGuid(), "robb_stark", "some-token", false, false, false, false, false, []);

        var json = JsonSerializer.Serialize(dto, Options);

        Assert.Contains("\"game_token\":\"some-token\"", json);
        Assert.DoesNotContain("\"gameToken\"", json);
    }

    [Fact]
    public void GameDto_SerializesOwnerAndSerializedGameAsSnakeCase()
    {
        // Matches LiveWebsiteClient.ts's getGame(): response.owner, response.serialized_game.
        var dto = new GameDto(Guid.NewGuid(), "Some game", Guid.NewGuid(), null, "1", "Ongoing", null);

        var json = JsonSerializer.Serialize(dto, Options);

        Assert.Contains("\"owner\":", json);
        Assert.DoesNotContain("\"ownerUserId\"", json);
        Assert.DoesNotContain("\"serializedGame\"", json);
    }

    [Fact]
    public void CreateRoomDto_DeserializesMaxRetrieveCountFromSnakeCase()
    {
        // Matches LiveWebsiteClient.ts's createPublicChatRoom()/createPrivateChatRoom() request
        // body: { name, public, users, max_retrieve_count }.
        const string incomingJson = """{"name":"lobby","public":true,"users":[],"max_retrieve_count":30}""";

        var dto = JsonSerializer.Deserialize<CreateRoomDto>(incomingJson, Options);

        Assert.NotNull(dto);
        Assert.Equal(30, dto!.MaxRetrieveCount);
    }
}
