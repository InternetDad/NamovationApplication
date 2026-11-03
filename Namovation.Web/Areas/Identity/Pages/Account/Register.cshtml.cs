using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Namovation.Web.Areas.Identity.Pages.Account;

public sealed class RegisterModel : PageModel
{
    public IActionResult OnGet() => RedirectToAction("Register", "Account", new { area = "" });
    public IActionResult OnPost() => RedirectToAction("Register", "Account", new { area = "" });
}
