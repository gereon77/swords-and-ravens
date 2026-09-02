using Soenneker.Validators.Email.Disposable.Online.Abstract;

namespace agot_bg_website.Services;

/// <summary>
/// Thin wrapper around Soenneker.Validators.Email.Disposable.Online (see LOCAL_DEV_VERIFICATION.md
/// "Disposable email" section) that fails open: if the underlying validator can't reach its
/// domain-list source (offline dev box, GitHub outage, ...), registration/email-change must not be
/// blocked outright just because the block-list couldn't be fetched. Only a confirmed match against
/// the downloaded disposable-domain list refuses the address.
/// </summary>
public class DisposableEmailChecker(IEmailDisposableOnlineValidator validator, ILogger<DisposableEmailChecker> logger)
{
    public async Task<bool> IsDisposableAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate() returns false when the domain IS in the disposable list, true when it
            // isn't (or no domain could be extracted), and null when the downloaded list was empty.
            var accepted = await validator.Validate(email, cancellationToken);
            return accepted == false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Disposable email domain check failed for {Email}; allowing it through (fail-open).", email);
            return false;
        }
    }
}
