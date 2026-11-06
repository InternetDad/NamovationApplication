using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namovation.Domain;
using Namovation.Infrastructure.Data;
using Namovation.Web.Models;

namespace Namovation.Web.Controllers;

public class HomeController(NamovationDbContext db, UserManager<NamovationUser> users, IWebHostEnvironment environment, IConfiguration configuration) : Controller
{
    public async Task<IActionResult> Index()
    {
        var metrics = new List<DashboardMetric>();
        var shortcuts = new List<DashboardShortcut>();
        var displayName = "";
        var authenticatorSetupPending = false;

        if (User.Identity?.IsAuthenticated == true)
        {
            var currentUser = await users.GetUserAsync(User);
            displayName = currentUser?.DisplayName ?? "";
            var userId = users.GetUserId(User)!;

            if (currentUser is not null)
                authenticatorSetupPending = await users.GetAuthenticationTokenAsync(currentUser, "Namovation", "AuthenticatorSetupPending") == "true";

            if (authenticatorSetupPending)
                shortcuts.Add(new("Finish authenticator setup", "Connect your authenticator app to complete account setup and unlock your employee workspace.", "Account", "SetupAuthenticator", "Continue setup"));

            if (!authenticatorSetupPending && User.IsInRole("Employee"))
            {
                var total = await db.Ideas.CountAsync(x => x.SubmitterId == userId);
                var active = await db.Ideas.CountAsync(x => x.SubmitterId == userId && x.Status != IdeaStatus.Repository1 && x.Status != IdeaStatus.Repository2 && x.Status != IdeaStatus.Repository3);
                metrics.Add(new("My ideas", total.ToString(), "All submissions"));
                metrics.Add(new("In progress", active.ToString(), "Moving through review"));
                shortcuts.Add(new("Submit an idea", "Choose the department for each idea; its manager will review it.", "Workflow", "Submit", "Start a submission"));
                shortcuts.Add(new("Track my ideas", "See the latest status and any available outcome.", "Workflow", "Mine", "View my ideas"));
            }
            if (!authenticatorSetupPending && User.IsInRole("InnovateManager"))
            {
                var pending = await db.Ideas.CountAsync(x => x.Status == IdeaStatus.Submitted && x.Department != null && x.Department.ManagerId == userId);
                var ready = await db.Ideas.CountAsync(x => x.Screening != null && x.Screening.ManagerId == userId && x.Status == IdeaStatus.BoardEvaluation && x.Evaluations.Count == EvaluationRules.RequiredBoardMembers && x.Result == null);
                var preparing = await db.MonthlyCompilations.CountAsync(x => x.CreatedById == userId && !x.ForwardedToBoard && x.Ideas.Any());
                metrics.Add(new("To screen", pending.ToString(), "New submissions"));
                metrics.Add(new("Ready to compile", ready.ToString(), "Three board reviews received"));
                metrics.Add(new("Monthly batches", preparing.ToString(), "Preparing for the board"));
                shortcuts.Add(new("Manager workspace", "Screen ideas, forward monthly batches, and record outcomes.", "Workflow", "Manager", "Open manager workspace"));
            }
            if (!authenticatorSetupPending && User.IsInRole("InnovateBoard"))
            {
                var pending = await db.Ideas.CountAsync(x => x.Status == IdeaStatus.BoardEvaluation && !x.Evaluations.Any(e => e.BoardMemberId == userId));
                metrics.Add(new("Awaiting my review", pending.ToString(), "Anonymized ideas"));
                shortcuts.Add(new("Board evaluations", "Review anonymized ideas and submit your independent scores.", "Workflow", "Board", "Open board reviews"));
            }
            if (!authenticatorSetupPending && User.IsInRole("Admin"))
            {
                metrics.Add(new("User accounts", (await users.Users.CountAsync()).ToString(), "Manage accounts and roles"));
                shortcuts.Add(new("User management", "Review self-registered accounts and assign application roles.", "Admin", "Users", "Manage users"));
                shortcuts.Add(new("Registration settings", "Choose whether new accounts must set up an authenticator app.", "Admin", "RegistrationSettings", "Configure registration"));
            }
        }

        var isAnonymous = User.Identity?.IsAuthenticated != true;
        var showFirstAdminSetup = (environment.IsDevelopment() || !string.IsNullOrWhiteSpace(configuration["INITIAL_ADMIN_SETUP_TOKEN"]))
            && isAnonymous
            && (await users.GetUsersInRoleAsync("Admin")).Count == 0;
        return View(new HomeDashboardModel(
            !isAnonymous,
            string.IsNullOrWhiteSpace(displayName) ? User.Identity?.Name ?? "there" : displayName,
            authenticatorSetupPending,
            isAnonymous,
            showFirstAdminSetup,
            metrics,
            shortcuts));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
