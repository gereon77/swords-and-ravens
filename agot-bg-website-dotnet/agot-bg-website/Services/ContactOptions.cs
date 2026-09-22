namespace agot_bg_website.Services;

/// <summary>
/// Settings for the public contact form (Pages/Contact.cshtml). Bound from the "Contact" section
/// of appsettings.json, overridable per deployment via the Contact__RecipientAddress /
/// Contact__MaxMessagesPerDay environment variables (see docker-compose.prod.yml and
/// .env.prod.example).
/// </summary>
public sealed class ContactOptions
{
    /// <summary>
    /// Where contact-form messages are delivered - a single address, or a ";"-separated list (e.g.
    /// to also loop in an interested mod). Blank disables the form entirely (the page then tells
    /// visitors it's unavailable), so a deployment that never configured it can't silently
    /// swallow messages. There's deliberately no default value here: no shared inbox exists to
    /// silently fall back to.
    /// </summary>
    public string RecipientAddress { get; set; } = string.Empty;

    /// <summary>How many messages a single visitor may send per (UTC) day.</summary>
    public int MaxMessagesPerDay { get; set; } = 2;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(RecipientAddress);

    /// <summary>
    /// Splits <see cref="RecipientAddress"/> on ";" into the individual addresses every contact
    /// message is Bcc'd to.
    /// </summary>
    public IReadOnlyList<string> GetRecipientAddresses() =>
        RecipientAddress.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
}
