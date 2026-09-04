using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace agot_bg_website.Services;

/// <summary>
/// API-based <see cref="IEmailSender"/> backed by Amazon SES's own HTTPS API (SendEmailV2), used
/// instead of the SMTP path (<see cref="SmtpEmailSender"/>) when the deployment host blocks
/// outbound SMTP ports (Dokku did) but SES is still the desired provider. Registered (see
/// Program.cs) when <c>Email:Ses:AccessKeyId</c> is configured.
///
/// This needs a different, separate credential type than <see cref="SmtpEmailSender"/>'s
/// <c>Email:Username</c>/<c>Email:Password</c>: SES's SMTP credentials are a username/password
/// pair usable only over SMTP, while its API requires an IAM access key ID + secret access key
/// (AWS SigV4-signed requests, handled here by the official <c>AWSSDK.SimpleEmailV2</c> package
/// rather than hand-rolled signing) - generate these from the same SES/IAM console under "Create
/// IAM user"/"Security credentials", granting the <c>ses:SendEmail</c> permission. See
/// README.md's "Email" section for the exact setup steps.
/// </summary>
public class SesApiEmailSender(IConfiguration configuration, ILogger<SesApiEmailSender> logger)
    : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var accessKeyId = configuration["Email:Ses:AccessKeyId"];
        var secretAccessKey = configuration["Email:Ses:SecretAccessKey"];
        var region = configuration["Email:Ses:Region"] ?? "eu-central-1";
        var fromAddress =
            configuration["Email:FromAddress"] ?? "Swords and Ravens <no-reply@winordie.net>";

        using var client = new AmazonSimpleEmailServiceV2Client(
            accessKeyId,
            secretAccessKey,
            RegionEndpoint.GetBySystemName(region)
        );

        var request = new SendEmailRequest
        {
            FromEmailAddress = fromAddress,
            Destination = new Destination { ToAddresses = [email] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject },
                    Body = new Body { Html = new Content { Data = htmlMessage } },
                },
            },
        };

        try
        {
            await client.SendEmailAsync(request);
            logger.LogInformation(
                "Sent email '{Subject}' to {Email} via Amazon SES API ({Region})",
                subject,
                email,
                region
            );
        }
        catch (Exception ex)
        {
            // Deliberately swallowed: same "never abort the caller" contract as
            // SmtpEmailSender/ApiEmailSender - see SmtpEmailSender.SendEmailAsync for the full
            // rationale.
            logger.LogError(
                ex,
                "Failed to send email '{Subject}' to {Email} via Amazon SES API ({Region}): {ErrorMessage}",
                subject,
                email,
                region,
                ex.Message
            );
        }
    }
}
