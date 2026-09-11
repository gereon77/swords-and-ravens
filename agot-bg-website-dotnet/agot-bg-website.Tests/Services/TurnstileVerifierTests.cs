using System.Net;
using System.Net.Http.Json;
using agot_bg_website.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace agot_bg_website.Tests.Services;

public class TurnstileVerifierTests
{
    private const string ExpectedHostname = "example.com";

    [Fact]
    public async Task VerifyAsync_WhenDisabled_AllowsRegistrationWithoutToken()
    {
        var verifier = CreateVerifier(
            new TurnstileOptions(),
            new FakeHandler(HttpStatusCode.InternalServerError)
        );

        Assert.True(await verifier.VerifyAsync(null, null, "register"));
    }

    [Fact]
    public async Task VerifyAsync_WhenEnabledWithoutToken_RejectsWithoutCallingProvider()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        var verifier = CreateVerifier(
            new TurnstileOptions { SiteKey = "site", SecretKey = "secret" },
            handler
        );

        Assert.False(await verifier.VerifyAsync(null, "127.0.0.1", "register"));
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task VerifyAsync_WhenProviderAcceptsMatchingActionAndHostname_ReturnsTrue()
    {
        var verifier = CreateVerifier(
            new TurnstileOptions { SiteKey = "site", SecretKey = "secret" },
            new FakeHandler(
                HttpStatusCode.OK,
                new
                {
                    success = true,
                    action = "register",
                    hostname = ExpectedHostname,
                }
            )
        );

        Assert.True(await verifier.VerifyAsync("token", "127.0.0.1", "register"));
    }

    [Fact]
    public async Task VerifyAsync_WhenProviderRejectsToken_ReturnsFalse()
    {
        var verifier = CreateVerifier(
            new TurnstileOptions { SiteKey = "site", SecretKey = "secret" },
            new FakeHandler(
                HttpStatusCode.OK,
                new
                {
                    success = false,
                    action = "register",
                    hostname = ExpectedHostname,
                }
            )
        );

        Assert.False(await verifier.VerifyAsync("token", "127.0.0.1", "register"));
    }

    [Fact]
    public async Task VerifyAsync_WhenActionDoesNotMatch_ReturnsFalse()
    {
        var verifier = CreateVerifier(
            new TurnstileOptions { SiteKey = "site", SecretKey = "secret" },
            new FakeHandler(
                HttpStatusCode.OK,
                new
                {
                    success = true,
                    action = "some-other-form",
                    hostname = ExpectedHostname,
                }
            )
        );

        Assert.False(await verifier.VerifyAsync("token", "127.0.0.1", "register"));
    }

    [Fact]
    public async Task VerifyAsync_WhenHostnameDoesNotMatch_ReturnsFalse()
    {
        var verifier = CreateVerifier(
            new TurnstileOptions { SiteKey = "site", SecretKey = "secret" },
            new FakeHandler(
                HttpStatusCode.OK,
                new
                {
                    success = true,
                    action = "register",
                    hostname = "attacker.example",
                }
            )
        );

        Assert.False(await verifier.VerifyAsync("token", "127.0.0.1", "register"));
    }

    [Fact]
    public async Task VerifyAsync_WhenPublicSiteUrlNotConfigured_FailsClosed()
    {
        var verifier = CreateVerifier(
            new TurnstileOptions { SiteKey = "site", SecretKey = "secret" },
            new FakeHandler(
                HttpStatusCode.OK,
                new
                {
                    success = true,
                    action = "register",
                    hostname = ExpectedHostname,
                }
            ),
            publicSiteUrl: null
        );

        Assert.False(await verifier.VerifyAsync("token", "127.0.0.1", "register"));
    }

    private static TurnstileVerifier CreateVerifier(
        TurnstileOptions options,
        HttpMessageHandler handler,
        string? publicSiteUrl = $"https://{ExpectedHostname}"
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["PublicSiteUrl"] = publicSiteUrl }
            )
            .Build();

        return new TurnstileVerifier(
            new HttpClient(handler),
            Options.Create(options),
            configuration,
            NullLogger<TurnstileVerifier>.Instance
        );
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, object? response = null)
        : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            WasCalled = true;
            var result = new HttpResponseMessage(statusCode);
            if (response is not null)
            {
                result.Content = JsonContent.Create(response);
            }

            await Task.CompletedTask;
            return result;
        }
    }
}
