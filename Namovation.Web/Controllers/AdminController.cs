using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namovation.Infrastructure.Data;
using Namovation.Web.Models;

namespace Namovation.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(UserManager<NamovationUser> users) : Controller
{
    private static readonly string[] ManagedRoles = ["Employee", "InnovateManager", "InnovateBoard", "Admin"];

    public async Task<IActionResult> Users()
    {
        var people = await users.Users.OrderBy(x => x.Email).ToListAsync();
        var rows = new List<AdminUserRow>(people.Count);
        foreach (var user in people)
        {
            var assigned = (await users.GetRolesAsync(user)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows.Add(new AdminUserRow(user.Id, user.Email ?? "", user.DisplayName, user.Designation, user.Location, assigned));
        }
        return View(rows);
    }

    [HttpGet]
    public IActionResult CreateUser() => View(new AdminCreateUserInput());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(AdminCreateUserInput input)
    {
        var selected = input.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Length == 0) ModelState.AddModelError(nameof(input.Roles), "Choose at least one role.");
        if (selected.Except(ManagedRoles, StringComparer.OrdinalIgnoreCase).Any()) ModelState.AddModelError(nameof(input.Roles), "One or more selected roles are invalid.");
        if (!ModelState.IsValid) return View(input);

        var email = input.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(input.Email), "An account with this email already exists.");
            return View(input);
        }

        var user = new NamovationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = input.DisplayName.Trim(),
            Designation = input.Designation?.Trim(),
            Location = input.Location?.Trim()
        };
        var createResult = await users.CreateAsync(user, input.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(input);
        }

        var roleResult = await users.AddToRolesAsync(user, selected);
        if (!roleResult.Succeeded)
        {
            await users.DeleteAsync(user);
            foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(input);
        }

        TempData["Message"] = $"Account created for {email}. Share the initial password with the user securely.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRoles(string userId, List<string>? roles)
    {
        var selected = (roles ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Except(ManagedRoles, StringComparer.OrdinalIgnoreCase).Any())
        {
            TempData["Error"] = "One or more selected roles are invalid.";
            return RedirectToAction(nameof(Users));
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Users));
        }

        var current = await users.GetRolesAsync(user);
        if (current.Contains("Admin", StringComparer.OrdinalIgnoreCase) && !selected.Contains("Admin", StringComparer.OrdinalIgnoreCase))
        {
            var admins = await users.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
            {
                TempData["Error"] = "Keep at least one Admin account assigned.";
                return RedirectToAction(nameof(Users));
            }
        }

        var remove = current.Except(selected, StringComparer.OrdinalIgnoreCase).ToArray();
        var add = selected.Except(current, StringComparer.OrdinalIgnoreCase).ToArray();
        if (remove.Length > 0)
        {
            var result = await users.RemoveFromRolesAsync(user, remove);
            if (!result.Succeeded) { TempData["Error"] = string.Join(" ", result.Errors.Select(x => x.Description)); return RedirectToAction(nameof(Users)); }
        }
        if (add.Length > 0)
        {
            var result = await users.AddToRolesAsync(user, add);
            if (!result.Succeeded) { TempData["Error"] = string.Join(" ", result.Errors.Select(x => x.Description)); return RedirectToAction(nameof(Users)); }
        }

        TempData["Message"] = $"Roles updated for {user.Email}.";
        return RedirectToAction(nameof(Users));
    }
}
