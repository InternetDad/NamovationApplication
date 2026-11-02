using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Namovation.Infrastructure.Data;

namespace Namovation.Web.Identity;

// Makes Identity UI registration usable for a local demo and persists the employee membership.
// Production stays on explicit role assignment so employee eligibility can be verified.
public sealed class DevelopmentEmployeeClaimsFactory(UserManager<NamovationUser> userManager,
    RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> options, IHostEnvironment environment)
    : UserClaimsPrincipalFactory<NamovationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NamovationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (environment.IsDevelopment() && (await UserManager.GetRolesAsync(user)).Count == 0 && await RoleManager.RoleExistsAsync("Employee"))
        {
            var result = await UserManager.AddToRoleAsync(user, "Employee");
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not assign the development Employee role: {string.Join(", ", result.Errors.Select(error => error.Description))}");
            identity.AddClaim(new Claim(ClaimTypes.Role, "Employee"));
        }
        return identity;
    }
}
