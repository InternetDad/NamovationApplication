using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Namovation.Web.Areas.Identity.Pages.Account;

public sealed class LoginModel : PageModel
{
    public IActionResult OnGet(string? returnUrl = null) => RedirectToAction("Login", "Account", new { area = "", returnUrl });
    public IActionResult OnPost(string? returnUrl = null) => RedirectToAction("Login", "Account", new { area = "", returnUrl });
}
