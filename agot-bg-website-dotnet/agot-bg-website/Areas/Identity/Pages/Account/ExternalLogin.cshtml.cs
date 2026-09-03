// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Auth;
using agot_bg_website.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace agot_bg_website.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ExternalLoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ExternalLoginModel> _logger;
        private readonly AccountLinkingService _accountLinkingService;
        private readonly DisposableEmailChecker _disposableEmailChecker;

        public ExternalLoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            ILogger<ExternalLoginModel> logger,
            IEmailSender emailSender,
            AccountLinkingService accountLinkingService,
            DisposableEmailChecker disposableEmailChecker
        )
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _logger = logger;
            _emailSender = emailSender;
            _accountLinkingService = accountLinkingService;
            _disposableEmailChecker = disposableEmailChecker;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ProviderDisplayName { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// True when the external provider supplied an already-verified email claim, in which case
        /// the email field is locked (read-only) instead of user-editable. This is both a UX signal
        /// and, together with the server-side re-derivation in <see cref="OnPostConfirmationAsync"/>,
        /// a defense against a user typing an arbitrary email (e.g. someone else's) to try to link
        /// their external login to a different, already-claimed account.
        /// </summary>
        public bool EmailIsReadOnly { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public IActionResult OnGet() => RedirectToPage("./Login");

        public IActionResult OnPost(string provider, string returnUrl = null)
        {
            // Request a redirect to the external login provider.
            var redirectUrl = Url.Page(
                "./ExternalLogin",
                pageHandler: "Callback",
                values: new { returnUrl }
            );
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(
                provider,
                redirectUrl
            );
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(
            string returnUrl = null,
            string remoteError = null
        )
        {
            returnUrl = ReturnUrlHelper.NormalizeAfterLogin(returnUrl ?? Url.Content("~/"));
            if (remoteError != null)
            {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            // Sign in the user with this external login provider if the user already has a login.
            // isPersistent: true - once a user signs in via an external provider (Discord/
            // Google/Facebook), keep them signed in with a long-lived cookie (see
            // ConfigureApplicationCookie's ExpireTimeSpan/SlidingExpiration in Program.cs) rather
            // than only for the current browser session, so they don't have to click through the
            // provider's consent screen again every time they close their browser.
            var result = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider,
                info.ProviderKey,
                isPersistent: true,
                bypassTwoFactor: true
            );
            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "{Name} logged in with {LoginProvider} provider.",
                    info.Principal.Identity.Name,
                    info.LoginProvider
                );
                return LocalRedirect(returnUrl);
            }
            if (result.IsNotAllowed)
            {
                // AppSignInManager.CanSignInAsync also refuses banned members here; tell them so
                // instead of falling through to the "let's create you an account" branch below,
                // which would otherwise be reached for any other NotAllowed reason too.
                var existingUser = await _userManager.FindByLoginAsync(
                    info.LoginProvider,
                    info.ProviderKey
                );
                if (
                    existingUser is not null
                    && await _userManager.IsInRoleAsync(existingUser, RoleNames.Banned)
                )
                {
                    return RedirectToPage("./Banned");
                }
            }
            if (result.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }
            else
            {
                // If the user does not have an account, then ask the user to create an account.
                ReturnUrl = returnUrl;
                ProviderDisplayName = info.ProviderDisplayName;
                if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
                {
                    Input = new InputModel
                    {
                        Email = info.Principal.FindFirstValue(ClaimTypes.Email),
                    };
                    EmailIsReadOnly = true;
                }
                return Page();
            }
        }

        public async Task<IActionResult> OnPostConfirmationAsync(string returnUrl = null)
        {
            returnUrl = ReturnUrlHelper.NormalizeAfterLogin(returnUrl ?? Url.Content("~/"));
            // Get the information about the user from the external login provider
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information during confirmation.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            // The email field is only meant to be editable when the external provider didn't supply
            // a verified email claim at all. When it did, always trust that claim over whatever was
            // posted: the "readonly" attribute on the form is just UX, and a user could otherwise
            // tamper with the request to submit an arbitrary (e.g. someone else's) email address to
            // try to have their external login linked onto a different, already-claimed account.
            if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
            {
                Input ??= new InputModel();
                Input.Email = info.Principal.FindFirstValue(ClaimTypes.Email);
                EmailIsReadOnly = true;
                ModelState.Clear();
                TryValidateModel(Input, nameof(Input));
            }

            if (ModelState.IsValid)
            {
                // Never create a second ApplicationUser for an email that's already registered
                // (local password account, previously-imported legacy account, or a different
                // OAuth provider): the external provider vouches for owning this email address, so
                // it's safe to just add this login to the existing account instead of erroring out
                // on a duplicate-email conflict or silently creating a duplicate identity. See the
                // "existing OAuth-linked / existing local account" note in MIGRATION_PLAN.md §5.3.
                //
                // IMPORTANT: only AccountLinkOutcome.Linked (a previously-imported, unclaimed legacy
                // user) is safe to auto-merge into. AccountLinkOutcome.ConflictAlreadyClaimed means an
                // *already claimed* account owns this email — auto-signing the current external login
                // into that account would be an account takeover, so it must surface as an error
                // instead (see AccountLinkingServiceTests.AlreadyClaimedUser_ReturnsConflict_DoesNotSilentlyMerge).
                var normalizedEmail = _userManager.NormalizeEmail(Input.Email);
                var linkResult = await _accountLinkingService.TryLinkByEmailAsync(normalizedEmail);

                if (linkResult.Outcome == AccountLinkOutcome.ConflictAlreadyClaimed)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "This email address is already associated with an existing account. "
                            + "Please sign in with your original method first, then add "
                            + $"{info.ProviderDisplayName} from your account settings."
                    );
                    ProviderDisplayName = info.ProviderDisplayName;
                    ReturnUrl = returnUrl;
                    return Page();
                }

                var existingUser =
                    linkResult.Outcome == AccountLinkOutcome.Linked ? linkResult.User : null;

                if (existingUser is not null)
                {
                    var addLoginResult = await _userManager.AddLoginAsync(existingUser, info);
                    if (addLoginResult.Succeeded)
                    {
                        _logger.LogInformation(
                            "Linked existing account {UserId} to new {Provider} login.",
                            existingUser.Id,
                            info.LoginProvider
                        );
                        await _signInManager.SignInAsync(
                            existingUser,
                            isPersistent: true,
                            info.LoginProvider
                        );
                        return LocalRedirect(returnUrl);
                    }

                    foreach (var error in addLoginResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    ProviderDisplayName = info.ProviderDisplayName;
                    ReturnUrl = returnUrl;
                    return Page();
                }

                if (await _disposableEmailChecker.IsDisposableAsync(Input.Email))
                {
                    ModelState.AddModelError(
                        "Input.Email",
                        "Throwaway/disposable email addresses aren't allowed. Please use an email address you can actually receive mail at."
                    );
                    ProviderDisplayName = info.ProviderDisplayName;
                    ReturnUrl = returnUrl;
                    return Page();
                }

                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                var result = await _userManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    result = await _userManager.AddLoginAsync(user, info);
                    if (result.Succeeded)
                    {
                        await _userManager.AddToRoleAsync(user, RoleNames.Member);
                        _logger.LogInformation(
                            "User created an account using {Name} provider.",
                            info.LoginProvider
                        );

                        var userId = await _userManager.GetUserIdAsync(user);
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                        var callbackUrl = Url.Page(
                            "/Account/ConfirmEmail",
                            pageHandler: null,
                            values: new
                            {
                                area = "Identity",
                                userId = userId,
                                code = code,
                            },
                            protocol: Request.Scheme
                        );

                        await _emailSender.SendEmailAsync(
                            Input.Email,
                            "Confirm your email",
                            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>."
                        );

                        // If account confirmation is required, we need to show the link if we don't have a real email sender
                        if (_userManager.Options.SignIn.RequireConfirmedAccount)
                        {
                            return RedirectToPage(
                                "./RegisterConfirmation",
                                new { Email = Input.Email }
                            );
                        }

                        await _signInManager.SignInAsync(
                            user,
                            isPersistent: true,
                            info.LoginProvider
                        );
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            ProviderDisplayName = info.ProviderDisplayName;
            ReturnUrl = returnUrl;
            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException(
                    $"Can't create an instance of '{nameof(ApplicationUser)}'. "
                        + $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor, or alternatively "
                        + $"override the external login page in /Areas/Identity/Pages/Account/ExternalLogin.cshtml"
                );
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException(
                    "The default UI requires a user store with email support."
                );
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
