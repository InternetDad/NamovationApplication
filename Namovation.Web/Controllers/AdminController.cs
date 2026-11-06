using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Namovation.Application;
using Namovation.Domain;
using Namovation.Infrastructure.Data;
using Namovation.Web.Models;

namespace Namovation.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(UserManager<NamovationUser> users, IRegistrationSettingsService registrationSettings, NamovationDbContext db) : Controller
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
    public async Task<IActionResult> Departments() => View(await BuildDepartmentsPageAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDepartment(DepartmentInput input)
    {
        var manager = await users.FindByIdAsync(input.ManagerId);
        if (!ModelState.IsValid || manager is null || !await users.IsInRoleAsync(manager, "InnovateManager"))
            TempData["Error"] = "Enter a department name and select a user with the InnovateManager role.";
        else
        {
            var normalizedName = input.Name.Trim().ToUpperInvariant();
            if (await db.Departments.AnyAsync(x => x.NormalizedName == normalizedName))
                TempData["Error"] = "A department with that name already exists.";
            else
            {
                db.Departments.Add(new Department { Name = input.Name.Trim(), NormalizedName = normalizedName, ManagerId = manager.Id });
                await db.SaveChangesAsync();
                TempData["Message"] = $"Department {input.Name.Trim()} was created and assigned to {manager.DisplayName}.";
            }
        }
        return RedirectToAction(nameof(Departments));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDepartmentManager(int id, DepartmentManagerInput input)
    {
        var department = await db.Departments.SingleOrDefaultAsync(x => x.Id == id);
        var manager = await users.FindByIdAsync(input.ManagerId);
        if (department is null)
            TempData["Error"] = "Department not found.";
        else if (!ModelState.IsValid || manager is null || !await users.IsInRoleAsync(manager, "InnovateManager"))
            TempData["Error"] = "Select a user with the InnovateManager role.";
        else
        {
            department.ManagerId = manager.Id;
            await db.SaveChangesAsync();
            TempData["Message"] = $"{department.Name} is now managed by {manager.DisplayName}.";
        }
        return RedirectToAction(nameof(Departments));
    }

    private async Task<DepartmentAdminPage> BuildDepartmentsPageAsync()
    {
        var managers = await DepartmentManagerChoicesAsync();
        var departments = await db.Departments.OrderBy(x => x.Name)
            .Select(x => new DepartmentAdminRow(x.Id, x.Name, x.ManagerId,
                db.Users.Where(u => u.Id == x.ManagerId).Select(u => u.DisplayName).FirstOrDefault() ?? "",
                x.Ideas.Count())).ToListAsync();
        return new DepartmentAdminPage(departments, managers);
    }

    private async Task<IReadOnlyList<SelectListItem>> DepartmentManagerChoicesAsync()
    {
        var managers = await users.GetUsersInRoleAsync("InnovateManager");
        return managers.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SelectListItem(string.IsNullOrWhiteSpace(x.Designation) ? x.DisplayName : $"{x.DisplayName} · {x.Designation}", x.Id)).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> RegistrationSettings()
    {
        var settings = await registrationSettings.GetAsync();
        return View(new AdminRegistrationSettingsViewModel(settings.AuthenticatorSetupRequired, settings.UpdatedAtUtc));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrationSettings(AdminRegistrationSettingsInput input)
    {
        await registrationSettings.UpdateAuthenticatorRequirementAsync(input.AuthenticatorSetupRequired, users.GetUserId(User)!);
        TempData["Message"] = input.AuthenticatorSetupRequired
            ? "Authenticator setup is required for new self-registrations."
            : "Authenticator setup is optional for new self-registrations.";
        return RedirectToAction(nameof(RegistrationSettings));
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
