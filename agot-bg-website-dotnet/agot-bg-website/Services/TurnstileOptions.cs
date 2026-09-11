namespace agot_bg_website.Services;

public sealed class TurnstileOptions
{
    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
