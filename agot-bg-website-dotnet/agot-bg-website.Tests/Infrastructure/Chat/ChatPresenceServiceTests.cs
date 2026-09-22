using agot_bg_website.Infrastructure.Chat;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure.Chat;

public class ChatPresenceServiceTests
{
    [Fact]
    public void CollapseConnections_CombinesMultipleTabsForOneUser()
    {
        var userId = Guid.NewGuid();
        var user = new ConnectedUserData("arya", false, true, "The Hand's Tourney");

        var users = ChatPresenceService.CollapseConnections([
            new ConnectedUserConnectionData(userId, user),
            new ConnectedUserConnectionData(userId, user),
        ]);

        var connectedUser = Assert.Single(users);
        Assert.Equal(userId, connectedUser.Key);
        Assert.Equal(user, connectedUser.Value);
    }

    [Fact]
    public void CollapseConnections_PreservesOtherUsersWhenOneConnectionIsAbsent()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var firstUser = new ConnectedUserData("arya", false, true, null);
        var secondUser = new ConnectedUserData("sansa", false, false, null);

        var users = ChatPresenceService.CollapseConnections([
            new ConnectedUserConnectionData(firstUserId, firstUser),
            new ConnectedUserConnectionData(secondUserId, secondUser),
        ]);

        Assert.Equal(2, users.Count);
        Assert.Equal(firstUser, users[firstUserId]);
        Assert.Equal(secondUser, users[secondUserId]);
    }

    [Fact]
    public void ConnectionLifetime_IsLongerThanHeartbeatInterval()
    {
        Assert.True(ChatPresenceService.ConnectionLifetime > TimeSpan.FromMinutes(5));
    }
}
