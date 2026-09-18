using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace agot_bg_website.Services;

/// <summary>
/// API-based <see cref="IEmailSender"/> backed by <a href="https://resend.com">Resend</a> — a
/// transactional-email HTTP API (as opposed to SMTP). Preferred over <see cref="SmtpEmailSender"/>
/// when configured (see Program.cs's conditional IEmailSender registration), since API-based
/// delivery avoids SMTP's port-25/587 blocking issues on some hosts and gives clearer
/// per-message delivery status. Only registered when <c>Email:Api:Key</c> is configured; the API
/// base URL itself is also configurable via <c>Email:Api:Host</c> (defaults to Resend's) so a
/// different API-based provider can be used without a code change. See README.md's "Email"
/// section for setup notes and cost/provider comparison.
/// </summary>
public class ApiEmailSender(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<ApiEmailSender> logger
) : IEmailSender, IBccEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var apiKey = configuration["Email:Api:Key"];
        var fromAddress =
            configuration["Email:FromAddress"]
            ?? "Swords and Ravens <no-reply@swordsandravens.net>";

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", apiKey) },
            Content = JsonContent.Create(
                new
                {
                    from = fromAddress,
                    to = new[] { email },
                    subject,
                    html = htmlMessage,
                }
            ),
        };

        try
        {
            using var response = await httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                logger.LogDebug("Sent email '{Subject}' to {Email} via Resend API", subject, email);
            }
            else
            {
                // Deliberately not thrown: same "never abort the caller" contract as
                // SmtpEmailSender - see its SendEmailAsync for the full rationale (Identity
                // account flows and NotificationsApi/ChatWebSocketApi batches must not fail
                // because of a delivery problem).
                var body = await response.Content.ReadAsStringAsync();
                logger.LogError(
                    "Failed to send email '{Subject}' to {Email} via Resend API: {StatusCode} {Body}",
                    subject,
                    email,
                    response.StatusCode,
                    body
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send email '{Subject}' to {Email} via Resend API: {ErrorMessage}",
                subject,
                email,
                ex.Message
            );
        }
    }

    /// <summary>
    /// Resend (like every mainstream transactional-email API) requires a non-empty "to", even for
    /// a Bcc-only send - see <see cref="EmailAddressHelper.ExtractBareAddress"/> for why the
    /// domain's own no-reply address is used as that placeholder.
    /// </summary>
    public async Task SendBccEmailAsync(
        IReadOnlyList<string> bccAddresses,
        string subject,
        string htmlMessage
    )
    {
        var apiKey = configuration["Email:Api:Key"];
        var fromAddress =
            configuration["Email:FromAddress"]
            ?? "Swords and Ravens <no-reply@swordsandravens.net>";
        var toAddress = EmailAddressHelper.ExtractBareAddress(fromAddress);

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", apiKey) },
            Content = JsonContent.Create(
                new
                {
                    from = fromAddress,
                    to = new[] { toAddress },
                    bcc = bccAddresses,
                    subject,
                    html = htmlMessage,
                }
            ),
        };

        try
        {
            using var response = await httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Sent Bcc email '{Subject}' to {Recipients} via Resend API",
                    subject,
                    string.Join(", ", bccAddresses)
                );
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogError(
                    "Failed to send Bcc email '{Subject}' to {Recipients} via Resend API: {StatusCode} {Body}",
                    subject,
                    string.Join(", ", bccAddresses),
                    response.StatusCode,
                    body
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send Bcc email '{Subject}' to {Recipients} via Resend API: {ErrorMessage}",
                subject,
                string.Join(", ", bccAddresses),
                ex.Message
            );
        }
    }
}
