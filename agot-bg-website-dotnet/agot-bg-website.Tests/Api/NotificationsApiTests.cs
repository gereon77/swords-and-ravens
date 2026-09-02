using agot_bg_website.Api;
using agot_bg_website.Domain;
using Xunit;

namespace agot_bg_website.Tests.Api;

public class NotificationsApiTests
{
    public static TheoryData<string, string> HtmlTemplateCases =>
        new()
        {
            { "notifyReadyToStart", "is ready to start:" },
            { "notifyYourTurn", "It's your turn to play in" },
            { "notifyBribeForSupport", "and now you can call for support or try to bribe your way there:" },
            { "notifyBattleResults", "Your battle in" },
            { "notifyNewVote", "a new vote has been started in" },
            { "notifyGameEnded", "has ended:" },
        };

    [Theory]
    [MemberData(nameof(HtmlTemplateCases))]
    public void BuildBodyHtml_RendersHtmlParagraphsAndClickableLink(
        string route,
        string expectedSnippet
    )
    {
        var user = new ApplicationUser { UserName = "Arya" };
        var game = new Game { Name = "Clash for Westeros" };
        const string gameUrl = "https://swordsandravens.net/play/123";

        var html = NotificationsApi.BuildBodyHtml(route, user, game, gameUrl);

        Assert.Contains("<p>Hello Arya,</p>", html);
        Assert.Contains(expectedSnippet, html);
        Assert.Contains("<p><a href=\"https://swordsandravens.net/play/123\">https://swordsandravens.net/play/123</a></p>", html);
        Assert.Contains("<p>Warmest regards,<br />Staff @ Swords and Ravens</p>", html);
    }

    [Fact]
    public void BuildBodyHtml_HtmlEncodesDynamicUserGameAndUrlContent()
    {
        var user = new ApplicationUser { UserName = "Player <One> & Co" };
        var game = new Game { Name = "Foo & <Bar>" };
        const string gameUrl = "https://example.com/play/123?name=foo&mode=\"pbem\"";

        var html = NotificationsApi.BuildBodyHtml("notifyReadyToStart", user, game, gameUrl);

        Assert.Contains("Hello Player &lt;One&gt; &amp; Co,", html);
        Assert.Contains("Your game &quot;Foo &amp; &lt;Bar&gt;&quot; is ready to start:", html);
        Assert.Contains(
            "<a href=\"https://example.com/play/123?name=foo&amp;mode=&quot;pbem&quot;\">https://example.com/play/123?name=foo&amp;mode=&quot;pbem&quot;</a>",
            html
        );
        Assert.DoesNotContain("Hello Player <One> & Co,", html);
        Assert.DoesNotContain("Your game &quot;Foo & <Bar>&quot; is ready to start:", html);
        Assert.DoesNotContain("<a href=\"https://example.com/play/123?name=foo&mode=\"pbem\"\">", html);
    }
}
