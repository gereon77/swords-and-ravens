using Microsoft.AspNetCore.Identity.UI.Services;

namespace agot_bg_website.Services;

/// <summary>
/// Fallback <see cref="IEmailSender"/> registered (see Program.cs) when neither an API-based
/// provider (<see cref="ApiEmailSender"/>, <c>Email:Api:Key</c>) nor SMTP
/// (<see cref="SmtpEmailSender"/>, <c>Email:Host</c>) is configured - the typical state for a
/// fresh local dev checkout before running smtp4dev or setting any <c>Email:*</c> user-secret.
/// Rather than crashing or silently dropping the message (ASP.NET Core Identity's own built-in
/// default sender does the latter), this logs the subject/recipient/body so a developer can
/// still see what would have been sent.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        logger.LogInformation(
            "Email sending is not configured (neither Email:Api:Key nor Email:Host is set); "
                + "would have sent '{Subject}' to {Email}:\n{HtmlMessage}",
            subject,
            email,
            htmlMessage
        );
        return Task.CompletedTask;
    }
}
