using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namovation.Application;
using Namovation.Infrastructure.Data;
using Namovation.Web.Models;
using QRCoder;

namespace Namovation.Web.Controllers;

public sealed class AccountController(UserManager<NamovationUser> users, RoleManager<IdentityRole> roles,
    SignInManager<NamovationUser> signIn, IWebHostEnvironment environment,
    IRegistrationSettingsService registrationSettings, ILogger<AccountController> logger, IConfiguration configuration) : Controller
{
    private const string RegistrationTokenProvider = "Namovation";
    private const string AuthenticatorSetupPendingToken = "AuthenticatorSetupPending";
    private static readonly SemaphoreSlim FirstAdminLock = new(1, 1);

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginInput { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid) return View(input);

        var user = await users.FindByEmailAsync(input.Email.Trim());
        var result = await signIn.PasswordSignInAsync(input.Email.Trim(), input.Password, input.RememberMe, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
            return RedirectToAction(nameof(LoginWithAuthenticator), new { input.ReturnUrl, input.RememberMe });

        if (result.Succeeded)
        {
            if (user is not null && await IsAuthenticatorSetupPendingAsync(user))
                return RedirectToAction(nameof(SetupAuthenticator));
            return RedirectAfterSignIn(input.ReturnUrl);
        }

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "This account is temporarily locked. Please try again later or contact an administrator.");
        else if (result.IsNotAllowed && user is not null && !user.EmailConfirmed)
            ModelState.AddModelError(string.Empty, "This account cannot sign in yet. Contact an administrator to check account access.");
        else
            ModelState.AddModelError(string.Empty, "The email or password is incorrect.");

        return View(input);
    }

    [HttpGet]
    public IActionResult LoginWithAuthenticator(string? returnUrl = null, bool rememberMe = false) =>
        View(new AuthenticatorLoginInput { ReturnUrl = returnUrl, RememberMe = rememberMe });

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-verification")]
    public async Task<IActionResult> LoginWithAuthenticator(AuthenticatorLoginInput input)
    {
        if (!ModelState.IsValid) return View(input);

        var result = input.UseRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(input.Code.Trim())
            : await signIn.TwoFactorAuthenticatorSignInAsync(input.Code.Replace(" ", "").Replace("-", "").Trim(), input.RememberMe, input.RememberMachine);

        if (result.Succeeded) return RedirectAfterSignIn(input.ReturnUrl);
        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "This account is temporarily locked. Please try again later or contact an administrator.");
        else
            ModelState.AddModelError(nameof(input.Code), input.UseRecoveryCode ? "That recovery code is invalid or has already been used." : "That authenticator code is invalid. Check the time on your device and try again.");
        return View(input);
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterInput());

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-verification")]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid) return View(input);

        var setupRequired = (await registrationSettings.GetAsync()).AuthenticatorSetupRequired;
        var email = input.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(input.Email), "An account with this email already exists. Try signing in.");
            return View(input);
        }

        var user = new NamovationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            DisplayName = input.DisplayName.Trim(),
            Designation = input.Designation?.Trim(),
            Location = input.Location?.Trim()
        };
        var created = await users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(input);
        }

        if (setupRequired)
        {
            var tokenResult = await users.SetAuthenticationTokenAsync(user, RegistrationTokenProvider, AuthenticatorSetupPendingToken, "true");
            if (!tokenResult.Succeeded)
            {
                await users.DeleteAsync(user);
                ModelState.AddModelError(string.Empty, "The account could not be prepared for authenticator setup. Please try again.");
                return View(input);
            }

            await signIn.SignInAsync(user, isPersistent: false);
            return RedirectToAction(nameof(SetupAuthenticator));
        }

        await AssignEmployeeRoleAsync(user);
        ViewBag.AuthenticatorSetupSkipped = true;
        return View("RegistrationComplete");
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> SetupAuthenticator()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.TwoFactorEnabled || !await IsAuthenticatorSetupPendingAsync(user))
            return RedirectToAction("Index", "Home");

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded)
            {
                TempData["Error"] = "The authenticator setup key could not be generated. Please contact an administrator.";
                return RedirectToAction("Index", "Home");
            }
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        return View(new AuthenticatorSetupViewModel(user.Email ?? "", key ?? ""));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> AuthenticatorQrCode()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.TwoFactorEnabled || !await IsAuthenticatorSetupPendingAsync(user))
            return NotFound();

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key)) return NotFound();

        var email = Uri.EscapeDataString(user.Email ?? user.UserName ?? "user");
        var issuer = Uri.EscapeDataString("Namovation");
        var provisioningUri = $"otpauth://totp/{issuer}:{email}?secret={key}&issuer={issuer}&digits=6&period=30&algorithm=SHA1";
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(provisioningUri, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(qrData).GetGraphic(8);

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        return File(png, "image/png");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-verification")]
    public async Task<IActionResult> SetupAuthenticator(AuthenticatorSetupInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.TwoFactorEnabled || !await IsAuthenticatorSetupPendingAsync(user))
            return RedirectToAction("Index", "Home");

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (!ModelState.IsValid)
            return View(new AuthenticatorSetupViewModel(user.Email ?? "", key ?? ""));

        var code = input.Code.Replace(" ", "").Replace("-", "").Trim();
        var valid = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);
        if (!valid)
        {
            ModelState.AddModelError(nameof(input.Code), "That code is invalid. Check that you entered the setup key correctly and try the latest code.");
            return View(new AuthenticatorSetupViewModel(user.Email ?? "", key ?? ""));
        }

        var enabled = await users.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Authenticator setup could not be saved. Please try again.");
            return View(new AuthenticatorSetupViewModel(user.Email ?? "", key ?? ""));
        }

        await users.RemoveAuthenticationTokenAsync(user, RegistrationTokenProvider, AuthenticatorSetupPendingToken);
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? [];
        await AssignEmployeeRoleAsync(user);
        await signIn.RefreshSignInAsync(user);
        return View("AuthenticatorSetupComplete", new AuthenticatorSetupCompleteViewModel(recoveryCodes.ToArray()));
    }

    private async Task<bool> IsAuthenticatorSetupPendingAsync(NamovationUser user) =>
        await users.GetAuthenticationTokenAsync(user, RegistrationTokenProvider, AuthenticatorSetupPendingToken) == "true";

    private IActionResult RedirectAfterSignIn(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");

    private async Task AssignEmployeeRoleAsync(NamovationUser user)
    {
        if (!await roles.RoleExistsAsync("Employee")) return;
        if (await users.IsInRoleAsync(user, "Employee")) return;
        var result = await users.AddToRoleAsync(user, "Employee");
        if (!result.Succeeded)
            logger.LogError("Account {UserId} was created but the Employee role could not be assigned.", user.Id);
    }

    [HttpGet]
    public async Task<IActionResult> SetupFirstAdmin()
    {
        if (!CanSetUpFirstAdmin()) return NotFound();
        if (await users.GetUsersInRoleAsync("Admin") is { Count: > 0 }) return RedirectToAction(nameof(Login));
        return View(new FirstAdminInput());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetupFirstAdmin(FirstAdminInput input)
    {
        if (!CanSetUpFirstAdmin()) return NotFound();
        if (!environment.IsDevelopment() && !IsValidSetupToken(input.SetupToken))
            ModelState.AddModelError(nameof(input.SetupToken), "The one-time setup key is incorrect.");
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

    private bool CanSetUpFirstAdmin() => environment.IsDevelopment()
        || !string.IsNullOrWhiteSpace(configuration["INITIAL_ADMIN_SETUP_TOKEN"]);

    private bool IsValidSetupToken(string supplied)
    {
        var expected = configuration["INITIAL_ADMIN_SETUP_TOKEN"];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrEmpty(supplied)) return false;
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = System.Text.Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
