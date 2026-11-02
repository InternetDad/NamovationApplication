using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Namovation.Infrastructure.Data;
using Namovation.Web.Models;

namespace Namovation.Web.Controllers;

public sealed class AccountController(UserManager<NamovationUser> users, RoleManager<IdentityRole> roles,
    SignInManager<NamovationUser> signIn, IWebHostEnvironment environment) : Controller
{
    private static readonly SemaphoreSlim FirstAdminLock = new(1, 1);

    [HttpGet]
    public async Task<IActionResult> SetupFirstAdmin()
    {
        if (!environment.IsDevelopment()) return NotFound();
        if (await users.GetUsersInRoleAsync("Admin") is { Count: > 0 }) return Redirect("/Identity/Account/Login");
        return View(new FirstAdminInput());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetupFirstAdmin(FirstAdminInput input)
    {
        if (!environment.IsDevelopment()) return NotFound();
        if (!ModelState.IsValid) return View(input);

        await FirstAdminLock.WaitAsync();
        try
        {
            if ((await users.GetUsersInRoleAsync("Admin")).Count > 0)
            {
                TempData["Error"] = "An Admin account already exists. Sign in and use User accounts and roles to manage users.";
                return RedirectToAction("Index", "Home");
            }
            if (!await roles.RoleExistsAsync("Admin"))
            {
                ModelState.AddModelError(string.Empty, "The Admin role is not initialized. Restart the application and try again.");
                return View(input);
            }

            var email = input.Email.Trim();
            var admin = new NamovationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = input.DisplayName.Trim() };
            var createResult = await users.CreateAsync(admin, input.Password);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return View(input);
            }

            var roleResult = await users.AddToRoleAsync(admin, "Admin");
            if (!roleResult.Succeeded)
            {
                await users.DeleteAsync(admin);
                foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return View(input);
            }

            await signIn.SignInAsync(admin, isPersistent: false);
            return RedirectToAction("Users", "Admin");
        }
        finally { FirstAdminLock.Release(); }
    }
}
