using Microsoft.AspNetCore.Identity;

namespace agot_bg_website.Infrastructure.Auth;

/// <summary>
/// Ensures the fixed set of roles used for authorization (<see cref="RoleNames.All"/>) exist.
/// Idempotent — safe to run on every startup, and keeps a freshly-migrated database (whose only
/// roles come from whatever the legacy Django groups table happened to contain, see
/// Snr.Migration) in sync with roles the app itself depends on (e.g. "Banned").
/// </summary>
public static class RoleSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var roleName in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }
    }
}
