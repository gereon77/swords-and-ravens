using System.Security.Claims;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using agot_bg_website.Pages;
using agot_bg_website.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace agot_bg_website.Tests.Pages;

/// <summary>
/// Covers the public contact form: Turnstile/honeypot protection, the "2 messages per day"
/// quota, the Bcc'd delivery to a ";"-separated recipient list, and - most importantly - that a
/// logged-in visitor's name/email can never be spoofed via the posted form (they're read-only in
/// the UI and always overwritten server-side from the real account). No live
/// Cloudflare/Redis/SMTP is needed - Turnstile is exercised through a fake siteverify HTTP
/// handler, the quota through a fake <see cref="IContactRateLimiter"/>, and delivery through a
/// fake <see cref="IBccEmailSender"/>. A real <see cref="UserManager{TUser}"/> (backed by EF
/// Core's InMemory provider) is used so <c>ContactModel</c>'s account lookup is exercised for
/// real, not stubbed.
/// </summary>
public class ContactModelTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly UserManager<ApplicationUser> _userManager;

    public ContactModelTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
        );
        services.AddLogging();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _userManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
    }

    public void Dispose() => _provider.Dispose();

    private sealed class FakeBccEmailSender : IBccEmailSender
    {
        public List<(
            IReadOnlyList<string> BccAddresses,
            string Subject,
            string HtmlMessage
        )> Sent { get; } = [];

        public Task SendBccEmailAsync(
            IReadOnlyList<string> bccAddresses,
            string subject,
            string htmlMessage
        )
        {
            Sent.Add((bccAddresses, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRateLimiter(bool allow) : IContactRateLimiter
    {
        public List<string> Keys { get; } = [];

        public Task<bool> TryAcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            return Task.FromResult(allow);
        }
    }

    private async Task<ApplicationUser> CreateUserAsync(string userName, string email)
    {
        var user = new ApplicationUser { UserName = userName, Email = email };
        var result = await _userManager.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(ApplicationUser user) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.UserName!),
                ],
                authenticationType: "Test"
            )
        );

    private ContactModel CreateModel(
        IBccEmailSender emailSender,
        IContactRateLimiter rateLimiter,
        string recipient = "staff@swordsandravens.net",
        ClaimsPrincipal? user = null
    )
    {
        // Turnstile stays unconfigured (blank keys), so TurnstileVerifier lets every request
        // through - exactly like local dev. The dedicated Turnstile tests cover the enabled path.
        var turnstileVerifier = new TurnstileVerifier(
            new HttpClient(),
            Options.Create(new TurnstileOptions()),
            new ConfigurationBuilder().Build(),
            NullLogger<TurnstileVerifier>.Instance
        );

        var model = new ContactModel(
            emailSender,
            Options.Create(
                new ContactOptions { RecipientAddress = recipient, MaxMessagesPerDay = 2 }
            ),
            rateLimiter,
            turnstileVerifier,
            _userManager,
            NullLogger<ContactModel>.Instance
        )
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()),
        };

        model.Input = new ContactModel.InputModel
        {
            Name = "Robb Stark",
            Email = "robb@winterfell.example",
            Subject = "A question",
            Message = "Can you help me with my game?",
        };

        return model;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Fact]
    public async Task OnPostAsync_ValidMessage_SendsEmailToConfiguredRecipient()
    {
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(emailSender, new FakeRateLimiter(allow: true));

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal(["staff@swordsandravens.net"], sent.BccAddresses);
        Assert.Equal("[Contact] A question", sent.Subject);
        Assert.Contains("robb@winterfell.example", sent.HtmlMessage);
        Assert.Equal("Thanks for reaching out! Your message has been sent.", model.StatusMessage);
    }

    [Fact]
    public async Task OnPostAsync_SemicolonSeparatedRecipients_AreAllBccd()
    {
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(
            emailSender,
            new FakeRateLimiter(allow: true),
            recipient: " staff@swordsandravens.net ; mod@swordsandravens.net "
        );

        await model.OnPostAsync();

        Assert.Equal(
            ["staff@swordsandravens.net", "mod@swordsandravens.net"],
            Assert.Single(emailSender.Sent).BccAddresses
        );
    }

    [Fact]
    public async Task OnPostAsync_DailyQuotaExhausted_DoesNotSendAndReturns429()
    {
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(emailSender, new FakeRateLimiter(allow: false));

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Empty(emailSender.Sent);
        Assert.Equal(StatusCodes.Status429TooManyRequests, model.Response.StatusCode);
        Assert.False(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostAsync_HoneypotFilled_DoesNotSend()
    {
        var emailSender = new FakeBccEmailSender();
        var rateLimiter = new FakeRateLimiter(allow: true);
        var model = CreateModel(emailSender, rateLimiter);
        model.Input.Website = "http://spam.example";

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Empty(emailSender.Sent);
        // The honeypot must short-circuit before the quota is touched, so a bot can't burn a real
        // visitor's allowance.
        Assert.Empty(rateLimiter.Keys);
    }

    [Fact]
    public async Task OnPostAsync_InvalidModel_DoesNotSend()
    {
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(emailSender, new FakeRateLimiter(allow: true));
        model.ModelState.AddModelError("Input.Email", "Invalid");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task OnPostAsync_NoRecipientConfigured_DoesNotSend()
    {
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(emailSender, new FakeRateLimiter(allow: true), recipient: "");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Empty(emailSender.Sent);
        Assert.False(model.ContactEnabled);
    }

    [Fact]
    public async Task OnPostAsync_AuthenticatedUser_QuotaFollowsTheAccount()
    {
        var user = await CreateUserAsync("robb_stark", "robb@stark.example");
        var rateLimiter = new FakeRateLimiter(allow: true);
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(emailSender, rateLimiter, user: AuthenticatedPrincipal(user));

        await model.OnPostAsync();

        Assert.Equal($"user:{user.Id}", Assert.Single(rateLimiter.Keys));
    }

    [Fact]
    public async Task OnPostAsync_AuthenticatedUser_IgnoresSpoofedNameAndEmail()
    {
        var user = await CreateUserAsync("robb_stark", "robb@stark.example");
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(
            emailSender,
            new FakeRateLimiter(allow: true),
            user: AuthenticatedPrincipal(user)
        );
        // Simulate an attacker overwriting the (should-be-readonly) Name/Email fields via
        // devtools before submitting, trying to impersonate someone else.
        model.Input.Name = "Not Robb At All";
        model.Input.Email = "impersonator@evil.example";

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Contains("robb_stark", sent.HtmlMessage);
        Assert.Contains("robb@stark.example", sent.HtmlMessage);
        Assert.DoesNotContain("Not Robb At All", sent.HtmlMessage);
        Assert.DoesNotContain("impersonator@evil.example", sent.HtmlMessage);
    }

    [Fact]
    public async Task OnPostAsync_AnonymousVisitor_UsesSubmittedNameAndEmail()
    {
        var emailSender = new FakeBccEmailSender();
        var model = CreateModel(emailSender, new FakeRateLimiter(allow: true));

        await model.OnPostAsync();

        var sent = Assert.Single(emailSender.Sent);
        Assert.Contains("Robb Stark", sent.HtmlMessage);
        Assert.Contains("robb@winterfell.example", sent.HtmlMessage);
        Assert.Contains("(not logged in)", sent.HtmlMessage);
    }

    [Fact]
    public async Task OnGetAsync_AuthenticatedUser_PrefillsNameAndEmailFromAccount()
    {
        var user = await CreateUserAsync("robb_stark", "robb@stark.example");
        var model = CreateModel(
            new FakeBccEmailSender(),
            new FakeRateLimiter(allow: true),
            user: AuthenticatedPrincipal(user)
        );

        await model.OnGetAsync();

        Assert.Equal("robb_stark", model.Input.Name);
        Assert.Equal("robb@stark.example", model.Input.Email);
    }

    [Fact]
    public void BuildRateLimitKey_AnonymousVisitor_UsesRemoteIp()
    {
        var key = ContactModel.BuildRateLimitKey(
            new ClaimsPrincipal(new ClaimsIdentity()),
            "203.0.113.7"
        );

        Assert.Equal("ip:203.0.113.7", key);
    }

    [Fact]
    public void BuildRateLimitKey_AnonymousVisitorWithoutIp_SharesOneBucket()
    {
        var key = ContactModel.BuildRateLimitKey(new ClaimsPrincipal(new ClaimsIdentity()), null);

        Assert.Equal("ip:unknown", key);
    }

    [Fact]
    public void BuildEmailHtml_EncodesVisitorSuppliedMarkup()
    {
        var html = ContactModel.BuildEmailHtml(
            new ContactModel.InputModel
            {
                Name = "<script>alert(1)</script>",
                Email = "spam@example.com",
                Subject = "<b>hi</b>",
                Message = "line one\nline two",
            },
            isAuthenticated: false
        );

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;hi&lt;/b&gt;", html);
        Assert.Contains("line one<br />line two", html);
        Assert.Contains("(not logged in)", html);
    }

    [Fact]
    public void BuildEmailHtml_Authenticated_LabelsAsVerifiedAccount()
    {
        var html = ContactModel.BuildEmailHtml(
            new ContactModel.InputModel
            {
                Name = "robb_stark",
                Email = "robb@stark.example",
                Subject = "hi",
                Message = "hello",
            },
            isAuthenticated: true
        );

        Assert.Contains("(verified account)", html);
    }

    [Fact]
    public void ContactRateLimiter_RedisKey_IsPerVisitorPerUtcDay()
    {
        var key = ContactRateLimiter.BuildRedisKey(
            "user:abc",
            new DateTimeOffset(2026, 9, 19, 1, 30, 0, TimeSpan.FromHours(2))
        );

        // 01:30 +02:00 on the 19th is still the 18th in UTC - the quota window is UTC, not local
        // time.
        Assert.Equal("contact:sent:2026-09-18:user:abc", key);
    }

    [Fact]
    public void ContactOptions_GetRecipientAddresses_SplitsTrimsAndDropsEmptyEntries()
    {
        var options = new ContactOptions { RecipientAddress = " a@x.com ;; b@x.com ; " };

        Assert.Equal(["a@x.com", "b@x.com"], options.GetRecipientAddresses());
    }
}
