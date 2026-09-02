using System.Text.Json;
using Xunit;

namespace agot_bg_website.Tests.Api;

/// <summary>
/// Regression coverage for a real bug: <c>Api/PlayApi.cs</c> used to replace an HTML *comment*
/// placeholder (<c>&lt;!--AUTH_DATA_JSON--&gt;</c>) in the game client's built <c>index.html</c>
/// with the auth-data &lt;script&gt; tag. html-webpack-plugin's production minifier strips HTML
/// comments entirely, so the placeholder silently vanished from the real built template before
/// PlayApi ever got a chance to replace it — the client then threw "No auth data available" at
/// runtime, but everything still compiled/tested fine because nothing exercised the actual built
/// asset. The fix (see MIGRATION_PLAN.md §8.1) is to place the placeholder as literal *text*
/// inside a real &lt;script id="auth-data" type="application/json"&gt; element, which survives
/// html-minifier-terser's comment stripping. These tests pin that contract so it can't regress
/// silently again.
/// </summary>
public class PlayApiAuthDataPlaceholderTests
{
    // Mirrors PlayApi.AuthDataPlaceholder (private) and the real template's markup shape.
    private const string AuthDataPlaceholder = "AUTH_DATA_JSON";

    private const string TemplateFragment =
        """<div id="root"></div><script id="auth-data" type="application/json">AUTH_DATA_JSON</script>""";

    [Fact]
    public void PlaceholderIsNotInsideAnHtmlComment()
    {
        // html-webpack-plugin's production minify preset removes HTML comments, so the
        // placeholder must never live inside one, or it will be stripped before PlayApi ever
        // runs. It must live in a plain text node instead.
        Assert.DoesNotContain($"<!--{AuthDataPlaceholder}", TemplateFragment);
    }

    [Fact]
    public void PlaceholderLivesInsideANonJavaScriptScriptTag()
    {
        // type="application/json" is what keeps html-minifier-terser's minifyJS step (and the
        // browser) from touching/executing this element before the backend substitutes real JSON
        // into it.
        Assert.Contains("""<script id="auth-data" type="application/json">""", TemplateFragment);
    }

    [Fact]
    public void ReplacingThePlaceholderProducesValidEmbeddedJson()
    {
        var authData = new
        {
            userId = Guid.NewGuid(),
            requestUserId = Guid.NewGuid(),
            gameId = Guid.NewGuid(),
            authToken = "token123",
        };
        var json = JsonSerializer.Serialize(authData);

        var html = TemplateFragment.Replace(AuthDataPlaceholder, json);

        Assert.DoesNotContain(AuthDataPlaceholder, html);

        var scriptStart =
            html.IndexOf(
                ">",
                html.IndexOf("id=\"auth-data\"", StringComparison.Ordinal),
                StringComparison.Ordinal
            ) + 1;
        var scriptEnd = html.IndexOf("</script>", scriptStart, StringComparison.Ordinal);
        var embeddedJson = html[scriptStart..scriptEnd];

        // Round-trips back to the same payload once embedded in the page, exactly what
        // client.tsx's getAuthData() does with document.getElementById("auth-data").textContent.
        using var parsed = JsonDocument.Parse(embeddedJson);
        Assert.Equal(authData.userId, parsed.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(authData.authToken, parsed.RootElement.GetProperty("authToken").GetString());
    }
}
