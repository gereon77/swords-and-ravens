using System.Net;

namespace agot_bg_website.Services;

/// <summary>
/// Shared HTML layout ("Hello {name}, ... Warmest regards, Staff @ Swords and Ravens") used to
/// bring ASP.NET Core Identity's plain default account emails (registration/email confirmation,
/// see Register.cshtml.cs, ExternalLogin.cshtml.cs, ResendEmailConfirmation.cshtml.cs,
/// Manage/Email.cshtml.cs) in line with the same look already used by every other Swords and
/// Ravens email (see NotificationsApi.BuildNotificationEmailHtml and
/// ChatWebSocketApi's private-message notification email).
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// Wraps <paramref name="bodyHtml"/> in the standard greeting/sign-off. If
    /// <paramref name="userName"/> is empty, or is just a copy of the account's email address
    /// (a brand-new account created via external login has its UserName defaulted to the email
    /// address until the user picks a real one - see ExternalLogin.cshtml.cs), there's no real
    /// name to greet the user by yet, so a bare "Hello," is used instead of
    /// "Hello someone@example.com,".
    /// </summary>
    public static string Build(string? userName, string? email, string bodyHtml)
    {
        var hasRealUserName =
            !string.IsNullOrWhiteSpace(userName)
            && !string.Equals(userName, email, StringComparison.OrdinalIgnoreCase);
        var greeting = hasRealUserName ? $"Hello {WebUtility.HtmlEncode(userName)}," : "Hello,";

        return $"""
            <p>{greeting}</p>
            {bodyHtml}
            <p>Warmest regards,<br />Staff @ Swords and Ravens</p>
            """;
    }
}
