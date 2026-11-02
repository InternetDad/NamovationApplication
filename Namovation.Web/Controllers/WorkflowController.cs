using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namovation.Application;
using Namovation.Domain;
using Namovation.Infrastructure.Data;
using Namovation.Web.Models;

namespace Namovation.Web.Controllers;

[Authorize]
public sealed class WorkflowController(NamovationDbContext db, IWorkflowService workflow, UserManager<NamovationUser> users) : Controller
{
    private string UserId => users.GetUserId(User)!;
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> Mine() => View(await db.Ideas.Where(x => x.SubmitterId == UserId).OrderByDescending(x => x.SubmittedAtUtc)
        .Select(x => new IdeaRow(x.Id, x.ReferenceNumber, x.Title, null, x.Status, x.SubmittedAtUtc, null, x.Evaluations.Count, x.Result == null ? null : x.Result.AverageTotal, x.Result == null ? null : x.Result.RewardEligible, x.Result == null ? null : x.Result.RewardOutcome, x.Result == null ? null : x.Result.Repository)).ToListAsync());

    [Authorize(Roles = "Employee")]
    public IActionResult Submit() => View(new IdeaInput());
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee")]
    public async Task<IActionResult> Submit(IdeaInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var idea = await workflow.SubmitIdeaAsync(UserId, input.Title, input.Description, input.ProblemStatement, input.ProposedBenefit);
        TempData["Message"] = $"Idea {idea.ReferenceNumber} was submitted.";
        // Email is an integration point: the application needs the organization's mail provider and approved sender configuration.
        return RedirectToAction(nameof(Mine));
    }

    [Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> Manager()
    {
        var pending = await Rows(db.Ideas.Where(x => x.Status == IdeaStatus.Submitted).OrderBy(x => x.SubmittedAtUtc));
        var ready = await Rows(db.Ideas.Where(x => x.Status == IdeaStatus.BoardEvaluation && x.Evaluations.Count == EvaluationRules.RequiredBoardMembers && x.Result == null));
        var repositories = await Rows(db.Ideas.Where(x => x.Result != null));
        var months = await db.MonthlyCompilations.Include(x => x.Ideas).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync();
        return View(new ManagerPage(pending, ready, months, repositories));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> Screen(int id, bool shortlist, string? rationale, string? boardTitle, string? boardDescription)
    {
        try { await workflow.ScreenAsync(id, UserId, shortlist, rationale, boardTitle, boardDescription); TempData["Message"] = shortlist ? "Shortlisted for the monthly board compilation." : "Filed in Repository 1."; }
        catch (WorkflowException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Manager));
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> Forward(int year, int month)
    {
        try { await workflow.ForwardMonthAsync(year, month, UserId); TempData["Message"] = "Monthly compilation forwarded to the board."; }
        catch (WorkflowException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Manager));
    }
    [Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> CompileReview(int id)
    {
        var idea = await db.Ideas.Include(x => x.Evaluations).SingleOrDefaultAsync(x => x.Id == id);
        if (idea is null || idea.Status != IdeaStatus.BoardEvaluation || idea.Evaluations.Count != EvaluationRules.RequiredBoardMembers || idea.Result is not null) return NotFound();
        var members = await db.Users.ToDictionaryAsync(x => x.Id, x => x.Email ?? x.DisplayName);
        var scores = idea.Evaluations.OrderBy(x => x.BoardMemberId).Select(x => new ScoreLine(members.GetValueOrDefault(x.BoardMemberId, "Board member"), x.Novelty, x.Feasibility, x.Relevance, x.BusinessImpact, x.CommercialViability)).ToList();
        return View(new CompilationReview(new IdeaRow(idea.Id, idea.ReferenceNumber, idea.Title, idea.Description, idea.Status, idea.SubmittedAtUtc, null, idea.Evaluations.Count, null, null, null, null), scores));
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> Compile(int id)
    {
        try { var result = await workflow.CompileAsync(id, UserId); TempData["Message"] = $"Compiled at {result.AverageTotal:0.00}/50. " + (result.RewardEligible ? "Awaiting the board and manager meeting decision." : "Filed in Repository 2."); }
        catch (WorkflowException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Manager));
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> RewardDecision(int id, RewardOutcome outcome, string? notes)
    {
        try { await workflow.DecideRewardAsync(id, UserId, outcome, notes); TempData["Message"] = "Meeting decision recorded."; }
        catch (WorkflowException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Manager));
    }

    [Authorize(Roles = "InnovateBoard")]
    public async Task<IActionResult> Board()
    {
        var items = await db.Ideas.Where(x => x.Status == IdeaStatus.BoardEvaluation)
            .Select(x => new BoardIdeaRow(x.Id, x.ReferenceNumber, x.Screening!.BoardFacingTitle!, x.Screening.BoardFacingDescription!, x.Evaluations.Any(e => e.BoardMemberId == UserId))).ToListAsync();
        return View(items);
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "InnovateBoard")]
    public async Task<IActionResult> Evaluate(ScoreInput input)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Every score must be between 1 and 10."; return RedirectToAction(nameof(Board)); }
        try { await workflow.EvaluateAsync(input.IdeaId, UserId, input.Novelty, input.Feasibility, input.Relevance, input.BusinessImpact, input.CommercialViability); TempData["Message"] = "Your evaluation was submitted."; }
        catch (WorkflowException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Board));
    }

    private async Task<IReadOnlyList<IdeaRow>> Rows(IQueryable<Idea> query) => await query
        .Select(x => new IdeaRow(x.Id, x.ReferenceNumber, x.Title, x.Description, x.Status, x.SubmittedAtUtc,
            db.Users.Where(u => u.Id == x.SubmitterId).Select(u => (u.DisplayName != "" ? u.DisplayName : u.Email) + " · " + (u.Designation ?? "Designation not set") + " · " + (u.Location ?? "Location not set")).FirstOrDefault(), x.Evaluations.Count,
            x.Result == null ? null : x.Result.AverageTotal, x.Result == null ? null : x.Result.RewardEligible,
            x.Result == null ? null : x.Result.RewardOutcome, x.Result == null ? null : x.Result.Repository,
            x.ProblemStatement, x.ProposedBenefit)).ToListAsync();
}
