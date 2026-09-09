// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using agot_bg_website.Domain;
using agot_bg_website.Infrastructure.Chat;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace agot_bg_website.Areas.Identity.Pages.Account
{
    public class LogoutModel(
        SignInManager<ApplicationUser> signInManager,
        ChatConnectionManager chatConnections,
        ILogger<LogoutModel> logger
    ) : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
        private readonly ChatConnectionManager _chatConnections = chatConnections;
        private readonly ILogger<LogoutModel> _logger = logger;

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            // Cookie auth alone doesn't touch any already-open chat WebSocket - the browser tab
            // typically stays open across logout, so without this the user would keep showing up
            // in the public room's "online users" list (ChatPresenceService) until they actually
            // close the tab. Capture the id before signing out since SignOutAsync only clears the
            // cookie for the *next* request; User still resolves for the rest of this one.
            var userId = _signInManager.UserManager.GetUserId(User);

            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");

            if (userId is not null && Guid.TryParse(userId, out var userGuid))
            {
                foreach (var connection in _chatConnections.GetConnectionsByUser(userGuid))
                {
                    connection.Socket.Abort();
                }
            }

            if (returnUrl != null)
            {
                return LocalRedirect(returnUrl);
            }
            else
            {
                // This needs to be a redirect so that the browser performs a new
                // request and the identity for the user gets updated.
                return RedirectToPage();
            }
        }
    }
}
