using System.Net.Mail;

namespace agot_bg_website.Services;

/// <summary>
/// Shared helper for the Bcc-capable senders (<see cref="IBccEmailSender"/> implementations).
/// </summary>
internal static class EmailAddressHelper
{
    /// <summary>
    /// Extracts the bare address ("user@domain") out of a possibly "Display Name
    /// &lt;user@domain&gt;"-formatted address string (e.g. <c>Email:FromAddress</c>), falling back
    /// to the input unchanged if it doesn't parse. Used as the placeholder "To" address for a
    /// Bcc-only send - transactional-email APIs require a non-empty "To" even when every real
    /// recipient is Bcc'd, and this domain's own no-reply address is always a syntactically valid
    /// choice, whether or not that specific mailbox is monitored: Bcc'd copies are delivered via
    /// their own independent SMTP recipient entries, so they don't depend on the "To" mailbox
    /// existing.
    /// </summary>
    public static string ExtractBareAddress(string address)
    {
        try
        {
            return new MailAddress(address).Address;
        }
        catch (FormatException)
        {
            return address;
        }
    }
}
