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

[Authorize]
public sealed class WorkflowController(NamovationDbContext db, IWorkflowService workflow, UserManager<NamovationUser> users, IIdeaDocumentStore documentStore, ILogger<WorkflowController> logger) : Controller
{
    private const long MaxSupportingDocumentBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".xlsx", ".pptx", ".txt" };
    private string UserId => users.GetUserId(User)!;
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> Mine() => View(await db.Ideas.Where(x => x.SubmitterId == UserId).OrderByDescending(x => x.SubmittedAtUtc)
        .Select(x => new IdeaRow(x.Id, x.ReferenceNumber, x.Title, null, x.Status, x.SubmittedAtUtc, null, x.Evaluations.Count, x.Result == null ? null : x.Result.AverageTotal, x.Result == null ? null : x.Result.RewardEligible, x.Result == null ? null : x.Result.RewardOutcome, x.Result == null ? null : x.Result.Repository, null, null, x.Attachment == null ? null : x.Attachment.Id, null, x.Department == null ? null : x.Department.Name)).ToListAsync());

    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> Submit() { await PopulateDepartmentsAsync(); return View(new IdeaInput()); }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee"), RequestSizeLimit(11 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> Submit(IdeaInput input)
    {
        var file = input.SupportingDocument;
        logger.LogInformation("Received an idea submission request. Supporting document attached: {HasDocument}; size: {SizeBytes} bytes.", file is not null, file?.Length ?? 0);
        if (file is not null)
        {
            var extension = Path.GetExtension(file.FileName);
            if (file.Length == 0) ModelState.AddModelError(nameof(input.SupportingDocument), "Choose a non-empty document.");
            else if (file.Length > MaxSupportingDocumentBytes) ModelState.AddModelError(nameof(input.SupportingDocument), "The document must be 10 MB or smaller.");
            else if (!AllowedDocumentExtensions.Contains(extension)) ModelState.AddModelError(nameof(input.SupportingDocument), "Use a PDF, Word, Excel, PowerPoint, or plain text file.");
        }
        var departmentId = input.DepartmentId.GetValueOrDefault();
        if (input.DepartmentId is null || !await db.Departments.AnyAsync(d => d.Id == departmentId && d.ManagerId != null))
            ModelState.AddModelError(nameof(input.DepartmentId), "Choose a department that has an assigned manager.");
        if (!ModelState.IsValid) { await PopulateDepartmentsAsync(input.DepartmentId); return View(input); }

        using var stream = file?.OpenReadStream();
        var upload = file is null ? null : new SupportingDocumentUpload(stream!, file.FileName, file.Length);
        Idea idea;
        try { idea = await workflow.SubmitIdeaAsync(UserId, departmentId, input.Title, input.Description, input.ProblemStatement, input.ProposedBenefit, upload); }
        catch (WorkflowException error)
        {
            ModelState.AddModelError(string.Empty, error.Message);
            await PopulateDepartmentsAsync(input.DepartmentId);
            return View(input);
        }
        catch (DbUpdateException error)
        {
            logger.LogError(error, "Database save failed while submitting an idea with a supporting document.");
            ModelState.AddModelError(string.Empty, "The idea could not be saved. Make sure the latest database migration has been applied, then try again.");
            await PopulateDepartmentsAsync(input.DepartmentId);
            return View(input);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogError(error, "Private supporting-document storage failed during idea submission.");
            ModelState.AddModelError(string.Empty, "The supporting document could not be saved. Check the document and try again.");
            await PopulateDepartmentsAsync(input.DepartmentId);
            return View(input);
        }
        catch (Exception error) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(error, "Unexpected failure during idea submission with a supporting document.");
            ModelState.AddModelError(string.Empty, "The idea could not be completed. The error has been logged; please try again or contact an administrator.");
            await PopulateDepartmentsAsync(input.DepartmentId);
            return View(input);
        }
        logger.LogInformation("Idea submission {ReferenceNumber} completed; redirecting to My ideas.", idea.ReferenceNumber);
        TempData["Message"] = $"Idea {idea.ReferenceNumber} was submitted.";
        // Email is an integration point: the application needs the organization's mail provider and approved sender configuration.
        return RedirectToAction(nameof(Mine));
    }

    [HttpGet]
    public async Task<IActionResult> SupportingDocument(int id)
    {
        var attachment = await db.IdeaAttachments.Include(x => x.Idea).ThenInclude(x => x.Screening).SingleOrDefaultAsync(x => x.Id == id);
        if (attachment is null) return NotFound();

        var isManager = User.IsInRole("InnovateManager") &&
            (attachment.Idea.Screening?.ManagerId == UserId || await db.Departments.AnyAsync(d => d.Id == attachment.Idea.DepartmentId && d.ManagerId == UserId));
        var isOwnerEmployee = User.IsInRole("Employee") && !User.IsInRole("InnovateBoard") && attachment.Idea.SubmitterId == UserId;
        var isAssignedBoardReviewer = User.IsInRole("InnovateBoard") && attachment.Idea.Status == IdeaStatus.BoardEvaluation;
        if (!isManager && !isOwnerEmployee && !isAssignedBoardReviewer) return Forbid();

        var stream = await documentStore.OpenReadAsync(attachment.StoredFileName);
        if (stream is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(stream, "application/octet-stream", attachment.OriginalFileName);
    }

    [Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> Manager()
    {
        var requeuedAddenda = await workflow.RequeueUnreviewedAutoAddendaAsync(UserId);
        if (requeuedAddenda > 0)
            TempData["Message"] = $"{requeuedAddenda} unreviewed idea(s) were returned to the monthly addendum queue. Forward them when ready.";
        var pending = await Rows(db.Ideas.Where(x => x.Status == IdeaStatus.Submitted && x.Department != null && x.Department.ManagerId == UserId).OrderBy(x => x.SubmittedAtUtc));
        // A Shortlisted idea attached to an already-forwarded compilation is a pending
        // addendum. Keep it visible to the manager until they explicitly forward it.
        var pendingAddenda = await Rows(db.Ideas.Where(x => x.Screening != null && x.Screening.ManagerId == UserId && x.Status == IdeaStatus.Shortlisted && x.MonthlyCompilation != null && x.MonthlyCompilation.ForwardedToBoard).OrderBy(x => x.SubmittedAtUtc));
        var awaitingBoard = await Rows(db.Ideas.Where(x => x.Screening != null && x.Screening.ManagerId == UserId && x.Status == IdeaStatus.BoardEvaluation && x.Evaluations.Count < EvaluationRules.RequiredBoardMembers).OrderBy(x => x.SubmittedAtUtc));
        var ready = await Rows(db.Ideas.Where(x => x.Screening != null && x.Screening.ManagerId == UserId && x.Status == IdeaStatus.BoardEvaluation && x.Evaluations.Count == EvaluationRules.RequiredBoardMembers && x.Result == null));
        var repositoryIdeas = db.Ideas.Where(x => x.Screening != null && x.Screening.ManagerId == UserId && x.Result != null);
        var repositories = (await Rows(repositoryIdeas.Where(x => x.Result!.Repository != RepositoryKind.Repository3)))
            .Concat(await Rows(repositoryIdeas.Where(x => x.Result!.Repository == RepositoryKind.Repository3), includeEmployeeDetails: true))
            .ToList();
        var months = await db.MonthlyCompilations.Where(x => x.CreatedById == UserId).Include(x => x.Ideas).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync();
        return View(new ManagerPage(pending, pendingAddenda, awaitingBoard, ready, months, repositories));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> Screen(int id, bool shortlist, string? rationale, string? boardDescription)
    {
        try
        {
            var addedAsBoardAddendum = await workflow.ScreenAsync(id, UserId, shortlist, rationale, boardDescription);
            TempData["Message"] = addedAsBoardAddendum
                ? "Shortlisted into the already-forwarded monthly compilation. It is waiting for you to forward the addendum."
                : shortlist ? "Shortlisted for the monthly board compilation." : "Filed in Repository 1.";
        }
        catch (WorkflowException e)
        {
            TempData["Error"] = e.Message;
        }
        return RedirectToAction(nameof(Manager));
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> Forward(int year, int month)
    {
        logger.LogInformation("Manager {ManagerId} requested forwarding for compilation {Year}-{Month:00}.", UserId, year, month);
        try
        {
            var isAddendum = await workflow.ForwardMonthAsync(year, month, UserId);
            TempData["Message"] = isAddendum ? "Monthly addendum forwarded to the board." : "Monthly compilation forwarded to the board.";
            logger.LogInformation("Compilation {Year}-{Month:00} forwarded by manager {ManagerId}. Addendum: {IsAddendum}.", year, month, UserId, isAddendum);
        }
        catch (WorkflowException e)
        {
            logger.LogWarning(e, "Forwarding compilation {Year}-{Month:00} was rejected for manager {ManagerId}.", year, month, UserId);
            TempData["Error"] = e.Message;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Forwarding compilation {Year}-{Month:00} failed for manager {ManagerId}.", year, month, UserId);
            TempData["Error"] = "The compilation could not be forwarded. Please try again; if this continues, check the application output log.";
        }
        return RedirectToAction(nameof(Manager));
    }
    [Authorize(Roles = "InnovateManager")]
    public async Task<IActionResult> CompileReview(int id)
    {
        var idea = await db.Ideas.Include(x => x.Evaluations).Include(x => x.Attachment).Include(x => x.Screening).SingleOrDefaultAsync(x => x.Id == id);
        if (idea is null || idea.Screening?.ManagerId != UserId || idea.Status != IdeaStatus.BoardEvaluation || idea.Evaluations.Count != EvaluationRules.RequiredBoardMembers || idea.Result is not null) return NotFound();
        var members = await db.Users.ToDictionaryAsync(x => x.Id, x => x.Email ?? x.DisplayName);
        var scores = idea.Evaluations.OrderBy(x => x.BoardMemberId).Select(x => new ScoreLine(members.GetValueOrDefault(x.BoardMemberId, "Board member"), x.Novelty, x.Feasibility, x.Relevance, x.BusinessImpact, x.CommercialViability)).ToList();
        return View(new CompilationReview(new IdeaRow(idea.Id, idea.ReferenceNumber, idea.Title, idea.Description, idea.Status, idea.SubmittedAtUtc, null, idea.Evaluations.Count, null, null, null, null, AttachmentId: idea.Attachment?.Id), scores));
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
            .OrderBy(x => x.SubmittedAtUtc)
            .Select(x => new BoardIdeaRow(x.Id, x.ReferenceNumber, x.Title, x.Description, x.SubmittedAtUtc,
                x.ProblemStatement, x.ProposedBenefit, x.Attachment == null ? null : x.Attachment.Id,
                x.Evaluations.Any(e => e.BoardMemberId == UserId),
                x.Screening == null ? null : x.Screening.BoardFacingDescription,
                x.Screening == null ? null : x.Screening.Rationale)).ToListAsync();
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

    private async Task PopulateDepartmentsAsync(int? selectedId = null) => ViewBag.Departments = new SelectList(
        await db.Departments.Where(d => d.ManagerId != null).OrderBy(d => d.Name).Select(d => new { d.Id, d.Name }).ToListAsync(), "Id", "Name", selectedId);

    private async Task<IReadOnlyList<IdeaRow>> Rows(IQueryable<Idea> query, bool includeEmployeeDetails = false) => await query
        .Select(x => new IdeaRow(x.Id, x.ReferenceNumber, x.Title, x.Description, x.Status, x.SubmittedAtUtc,
            includeEmployeeDetails ? db.Users.Where(u => u.Id == x.SubmitterId).Select(u => (u.DisplayName != "" ? u.DisplayName : u.Email) + " · " + (u.Designation ?? "Designation not set") + " · " + (u.Location ?? "Location not set")).FirstOrDefault() : null, x.Evaluations.Count,
            x.Result == null ? null : x.Result.AverageTotal, x.Result == null ? null : x.Result.RewardEligible,
            x.Result == null ? null : x.Result.RewardOutcome, x.Result == null ? null : x.Result.Repository,
            x.ProblemStatement, x.ProposedBenefit, x.Attachment == null ? null : x.Attachment.Id,
            x.Evaluations.Count == EvaluationRules.RequiredBoardMembers
                ? x.Evaluations.Average(e => (decimal)(e.Novelty + e.Feasibility + e.Relevance + e.BusinessImpact + e.CommercialViability))
                : (decimal?)null, x.Department == null ? null : x.Department.Name)).ToListAsync();
}
