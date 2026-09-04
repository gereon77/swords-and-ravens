using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace agot_bg_website.Services;

/// <summary>
/// SMTP-backed <see cref="IEmailSender"/>, used both by the built-in Identity UI (password
/// reset/email confirmation/email change) and by <c>NotificationsApi</c>'s game-notification
/// endpoints — see MIGRATION_PLAN.md §6/§9.1. Registered (see Program.cs) when <c>Email:Host</c>
/// is configured and no API-based provider (<see cref="ApiEmailSender"/>, <c>Email:Api:Key</c>)
/// is; otherwise <see cref="LoggingEmailSender"/> or <see cref="ApiEmailSender"/> is registered
/// instead, so local dev doesn't need a working mail server.
/// </summary>
public class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var host = configuration["Email:Host"];
        if (string.IsNullOrEmpty(host))
        {
            logger.LogWarning(
                "Email:Host is not configured; not sending '{Subject}' to {Email}",
                subject,
                email
            );
            return;
        }

        var port = int.TryParse(configuration["Email:Port"], out var parsedPort) ? parsedPort : 587;
        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];
        var fromAddress = configuration["Email:FromAddress"] ?? username ?? "no-reply@winordie.net";
        // Defaults to true for real SMTP providers; local test catchers (e.g. smtp4dev) usually
        // don't offer TLS on their plain SMTP port, so local dev sets Email:EnableSsl=false via
        // user-secrets — see LOCAL_DEV_VERIFICATION.md's "Email (local testing)" section.
        var enableSsl =
            !bool.TryParse(configuration["Email:EnableSsl"], out var parsedEnableSsl)
            || parsedEnableSsl;

        using var client = new SmtpClient(host, port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = enableSsl,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrEmpty(username)
                ? null
                : new NetworkCredential(username, password),
        };

        using var message = new MailMessage(fromAddress, email, subject, htmlMessage)
        {
            IsBodyHtml = true,
        };

        try
        {
            await client.SendMailAsync(message);
            logger.LogDebug(
                "Sent email '{Subject}' to {Email} via {Host}:{Port} (SSL={EnableSsl})",
                subject,
                email,
                host,
                port,
                enableSsl
            );
        }
        catch (Exception ex)
        {
            // Deliberately swallowed: a failed send (SMTP timeout, DNS failure, provider outage,
            // etc.) must never abort the caller's request. Callers include user-facing flows
            // (Register/ForgotPassword/ResendEmailConfirmation/Manage/Email, where the account
            // action itself has already succeeded by the time we try to email) and the
            // game server's NotificationsApi/ChatWebSocketApi raven notifications, where one
            // recipient's failed send must not stop the rest of the batch or crash the request.
            // Delivery failures are only observable via this log entry.
            logger.LogError(
                ex,
                "Failed to send email '{Subject}' to {Email} via {Host}:{Port} (SSL={EnableSsl}): {ErrorMessage}",
                subject,
                email,
                host,
                port,
                enableSsl,
                ex.Message
            );
        }
    }
}
