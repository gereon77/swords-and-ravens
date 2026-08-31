using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace agot_bg_website.Services;

/// <summary>
/// SMTP-backed <see cref="IEmailSender"/>, used both by the built-in Identity UI (password
/// reset/email confirmation/email change) and by <c>NotificationsApi</c>'s game-notification
/// endpoints — see MIGRATION_PLAN.md §6/§9.1. Only registered when <c>Email:Host</c> is
/// configured (see Program.cs); otherwise Identity's own default no-op sender is used, same
/// "only wire it up when configured" pattern used for the OAuth providers, so local dev doesn't
/// need a working mail server.
/// </summary>
public class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var host = configuration["Email:Host"];
        if (string.IsNullOrEmpty(host))
        {
            logger.LogWarning("Email:Host is not configured; not sending '{Subject}' to {Email}", subject, email);
            return;
        }

        var port = int.TryParse(configuration["Email:Port"], out var parsedPort) ? parsedPort : 587;
        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];
        var fromAddress = configuration["Email:FromAddress"] ?? username ?? "no-reply@swordsandravens.net";
        // Defaults to true for real SMTP providers; local test catchers (e.g. smtp4dev) usually
        // don't offer TLS on their plain SMTP port, so local dev sets Email:EnableSsl=false via
        // user-secrets — see LOCAL_DEV_VERIFICATION.md's "Email (local testing)" section.
        var enableSsl = !bool.TryParse(configuration["Email:EnableSsl"], out var parsedEnableSsl) || parsedEnableSsl;

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = string.IsNullOrEmpty(username) ? null : new NetworkCredential(username, password)
        };

        using var message = new MailMessage(fromAddress, email, subject, htmlMessage)
        {
            IsBodyHtml = false
        };

        await client.SendMailAsync(message);
    }
}
