using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace agot_bg_website.Services;

/// <summary>
/// Validates a Cloudflare Turnstile response token via the canonical server-side siteverify
/// contract (https://developers.cloudflare.com/turnstile/get-started/server-side-validation/):
/// require <c>success</c>, the caller's expected <c>action</c>, and a response <c>hostname</c>
/// matching this deployment's own public hostname (derived from the already-per-environment
/// <c>PublicSiteUrl</c> setting - see appsettings.{Environment}.json) rather than trusting a bare
/// boolean. This stops a leaked sitekey/response token pair from being replayed against a
/// different action or a different site that happens to share the same widget.
/// </summary>
public sealed class TurnstileVerifier
{
    private const string SiteVerifyEndpoint =
        "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly HttpClient _httpClient;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileVerifier> _logger;
    private readonly string? _expectedHostname;

    public TurnstileVerifier(
        HttpClient httpClient,
        IOptions<TurnstileOptions> options,
        IConfiguration configuration,
        ILogger<TurnstileVerifier> logger
    )
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _expectedHostname = Uri.TryCreate(
            configuration["PublicSiteUrl"],
            UriKind.Absolute,
            out var publicSiteUri
        )
            ? publicSiteUri.Host
            : null;
    }

    public bool IsEnabled => _options.IsEnabled;

    public string SiteKey => _options.SiteKey;

    public async Task<bool> VerifyAsync(
        string? responseToken,
        string? remoteIp,
        string expectedAction,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(responseToken))
        {
            return false;
        }

        if (_expectedHostname is null)
        {
            // PublicSiteUrl is always configured (see appsettings.json/appsettings.{Environment}.json)
            // - treat a missing/invalid value as a misconfiguration and fail closed rather than
            // skip the hostname check silently.
            _logger.LogWarning(
                "Turnstile verification rejected: PublicSiteUrl is not configured, so the response hostname can't be validated."
            );
            return false;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                SiteVerifyEndpoint,
                new
                {
                    secret = _options.SecretKey,
                    response = responseToken,
                    remoteip = remoteIp,
                },
                cancellationToken
            );
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TurnstileVerificationResponse>(
                cancellationToken
            );
            if (result is null || !result.Success)
            {
                return false;
            }

            if (!string.Equals(result.Action, expectedAction, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Turnstile verification rejected: action mismatch (expected {ExpectedAction}, got {ActualAction}).",
                    expectedAction,
                    result.Action
                );
                return false;
            }

            if (
                !string.Equals(
                    result.Hostname,
                    _expectedHostname,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                _logger.LogWarning(
                    "Turnstile verification rejected: hostname mismatch (expected {ExpectedHostname}, got {ActualHostname}).",
                    _expectedHostname,
                    result.Hostname
                );
                return false;
            }

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Turnstile verification failed.");
            return false;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Turnstile verification returned invalid data.");
            return false;
        }
    }

    private sealed class TurnstileVerificationResponse
    {
        public bool Success { get; init; }

        public string? Action { get; init; }

        public string? Hostname { get; init; }
    }
}
