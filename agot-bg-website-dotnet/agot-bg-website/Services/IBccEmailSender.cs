namespace agot_bg_website.Services;

/// <summary>
/// Sends an email where every recipient is Bcc'd (so recipients never see each other's address).
/// Used only by the public contact form (Pages/Contact.cshtml.cs) for its ";"-separated
/// Contact:RecipientAddress list - a separate interface from
/// <see cref="Microsoft.AspNetCore.Identity.UI.Services.IEmailSender"/> because that one only
/// takes a single "To" recipient, and most transactional-email APIs require a non-empty "To" even
/// for a Bcc-only send (see each implementation for how it fills that field). Registered with the
/// same provider precedence as IEmailSender (Program.cs): Amazon SES &gt; a generic API provider
/// (Resend) &gt; SMTP &gt; logging fallback.
/// </summary>
public interface IBccEmailSender
{
    Task SendBccEmailAsync(IReadOnlyList<string> bccAddresses, string subject, string htmlMessage);
}
