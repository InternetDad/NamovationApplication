using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using Namovation.Application;
using Namovation.Domain;
using Namovation.Infrastructure.Data;

namespace Namovation.Infrastructure.Services;

public sealed class WorkflowService(NamovationDbContext db, IIdeaDocumentStore documentStore, ILogger<WorkflowService> logger) : IWorkflowService
{
    private const long MaxSupportingDocumentBytes = 10 * 1024 * 1024;

    public async Task<Idea> SubmitIdeaAsync(string employeeId, int departmentId, string title, string description, string? problem, string? benefit, SupportingDocumentUpload? attachment = null, CancellationToken ct = default)
    {
        if (!await db.Departments.AnyAsync(x => x.Id == departmentId, ct))
            throw new WorkflowException("Choose an active department before submitting your idea.");
        if (attachment is not null && (attachment.SizeBytes <= 0 || attachment.SizeBytes > MaxSupportingDocumentBytes))
            throw new WorkflowException("The supporting document must be smaller than 10 MB.");

        var idea = new Idea {
            ReferenceNumber = $"N-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..15].ToUpperInvariant(),
            SubmitterId = employeeId, DepartmentId = departmentId, Title = title.Trim(), Description = description.Trim(),
            ProblemStatement = problem?.Trim(), ProposedBenefit = benefit?.Trim(), SubmittedAtUtc = DateTime.UtcNow,
            Status = IdeaStatus.Submitted
        };
        idea.StatusHistory.Add(Event(idea, idea.Status, employeeId, "Idea submitted"));

        string? storedFileName = null;
        if (attachment is not null)
        {
            var extension = Path.GetExtension(attachment.OriginalFileName).ToLowerInvariant();
            logger.LogInformation("Saving supporting document; {SizeBytes} bytes, extension {Extension}.", attachment.SizeBytes, extension);
            storedFileName = await documentStore.SaveAsync(attachment.Content, extension, ct);
            logger.LogInformation("Supporting document was written to private storage.");
            var originalFileName = Path.GetFileName(attachment.OriginalFileName);
            originalFileName = new string(originalFileName.Where(c => !char.IsControl(c)).ToArray());
            if (originalFileName.Length > 255) originalFileName = originalFileName[^255..];
            idea.Attachment = new IdeaAttachment
            {
                OriginalFileName = originalFileName,
                StoredFileName = storedFileName,
                ContentType = GetDocumentContentType(extension),
                SizeBytes = attachment.SizeBytes
            };
        }

        db.Ideas.Add(idea);
        try
        {
            logger.LogInformation("Saving idea {ReferenceNumber} and its attachment metadata to PostgreSQL.", idea.ReferenceNumber);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Idea {ReferenceNumber} was saved successfully.", idea.ReferenceNumber);
        }
        catch
        {
            if (storedFileName is not null) await documentStore.DeleteAsync(storedFileName, ct);
            throw;
        }
        return idea;
    }

    private static string GetDocumentContentType(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    public async Task<bool> ScreenAsync(int ideaId, string managerId, bool shortlist, string? rationale, string? boardDescription, CancellationToken ct = default)
    {
        var addendumRequiresForwarding = false;
        var idea = await db.Ideas.Include(x => x.Screening).Include(x => x.MonthlyCompilation)
            .SingleOrDefaultAsync(x => x.Id == ideaId, ct) ?? throw new WorkflowException("Idea not found.");
        if (idea.DepartmentId is not int departmentId ||
            !await db.Departments.AnyAsync(x => x.Id == departmentId && x.ManagerId == managerId, ct))
            throw new WorkflowException("This idea belongs to a department you do not manage.");
        if (idea.Status != IdeaStatus.Submitted) throw new WorkflowException("Only newly submitted ideas can be screened.");
        idea.Screening = new Screening { ManagerId = managerId, Shortlisted = shortlist, Rationale = rationale?.Trim(), ScreenedAtUtc = DateTime.UtcNow };
        if (shortlist)
        {
            if (string.IsNullOrWhiteSpace(boardDescription)) throw new WorkflowException("Provide the manager's description that the board will review.");
            idea.Screening.BoardFacingDescription = boardDescription.Trim();
            var month = idea.SubmittedAtUtc;
            var batch = await db.MonthlyCompilations.SingleOrDefaultAsync(x => x.Year == month.Year && x.Month == month.Month && x.CreatedById == managerId, ct);
            if (batch is null) { batch = new MonthlyCompilation { Year = month.Year, Month = month.Month, CreatedById = managerId }; db.MonthlyCompilations.Add(batch); }
            idea.MonthlyCompilation = batch;

            // An open batch follows the regular monthly flow. A forwarded batch has already
            // had its regular forward action, so keep this idea Shortlisted until the manager
            // explicitly forwards the new idea as an addendum.
            addendumRequiresForwarding = batch.ForwardedToBoard;
            idea.Status = IdeaStatus.Shortlisted;
        }
        else
        {
            idea.Status = IdeaStatus.Repository1;
            idea.Result = new IdeaCompilation { EvaluationCount = 0, AverageTotal = 0, RewardEligible = false, Repository = RepositoryKind.Repository1, CompiledById = managerId };
        }
        var historyNote = addendumRequiresForwarding
            ? $"Shortlisted after the {idea.SubmittedAtUtc:yyyy-MM} monthly compilation was forwarded; pending manager forwarding as an addendum. {rationale}".Trim()
            : rationale;
        idea.StatusHistory.Add(Event(idea, idea.Status, managerId, historyNote));
        await db.SaveChangesAsync(ct);
        return addendumRequiresForwarding;
    }

    public async Task<int> RequeueUnreviewedAutoAddendaAsync(string managerId, CancellationToken ct = default)
    {
        // Compatibility repair: an earlier version sent late addenda straight to the board.
        // Return only those with no board evaluations to the manager queue; never roll back
        // an idea that a board member has already evaluated.
        var priorAutoAddenda = await db.Ideas
            .Include(x => x.MonthlyCompilation)
            .Include(x => x.StatusHistory)
            .Where(x => x.Status == IdeaStatus.BoardEvaluation
                && !x.Evaluations.Any()
                && x.MonthlyCompilation != null
                && x.MonthlyCompilation.CreatedById == managerId
                && x.MonthlyCompilation.ForwardedToBoard
                && x.StatusHistory.Any(e => e.Note != null && e.Note.Contains("added to board evaluation as an addendum"))
                // Old history remains after an idea is manually forwarded. Without this
                // exclusion the manager page's compatibility cleanup immediately puts
                // the successfully forwarded addendum back into Shortlisted status.
                && !x.StatusHistory.Any(e => e.Note != null && e.Note.StartsWith("Forwarded as an addendum to ")))
            .ToListAsync(ct);
        foreach (var idea in priorAutoAddenda)
        {
            idea.Status = IdeaStatus.Shortlisted;
            var month = idea.MonthlyCompilation!;
            idea.StatusHistory.Add(Event(idea, idea.Status, "system", $"Returned to the manager's addendum queue for the already-forwarded {month.Year}-{month.Month:00} compilation; explicit forwarding is required."));
        }

        if (priorAutoAddenda.Count > 0) await db.SaveChangesAsync(ct);
        return priorAutoAddenda.Count;
    }

    public async Task<bool> ForwardMonthAsync(int year, int month, string managerId, CancellationToken ct = default)
    {
        if (month is < 1 or > 12) throw new WorkflowException("Choose a valid month.");
        var batch = await db.MonthlyCompilations
            .SingleOrDefaultAsync(x => x.Year == year && x.Month == month && x.CreatedById == managerId, ct) ?? throw new WorkflowException("No screened ideas are ready for this month.");

        // For a normal monthly forward, every idea in the batch must be screened first.
        // For an addendum, the batch itself stays Forwarded; only newly shortlisted ideas
        // are selected below, so previously forwarded/evaluated ideas are left untouched.
        var isAddendum = batch.ForwardedToBoard;
        // Query the idea rows directly instead of relying on the batch's navigation
        // collection. This makes the persisted status the source of truth for which
        // ideas this click will forward.
        var ideasToForward = await db.Ideas
            .Where(i => i.MonthlyCompilationId == batch.Id && i.Status == IdeaStatus.Shortlisted)
            .ToListAsync(ct);
        var batchIdeaCount = await db.Ideas.CountAsync(i => i.MonthlyCompilationId == batch.Id, ct);
        logger.LogInformation("Forward request for {Year}-{Month:00}: batch {BatchId}, already forwarded: {AlreadyForwarded}, ideas in batch: {BatchIdeaCount}, shortlisted to forward: {PendingCount}.", year, month, batch.Id, isAddendum, batchIdeaCount, ideasToForward.Count);
        if (ideasToForward.Count == 0) throw new WorkflowException(isAddendum
            ? "There are no pending shortlisted ideas to forward as an addendum."
            : "There are no shortlisted ideas ready for this month.");
        if (!isAddendum && await db.Ideas.AnyAsync(i => i.MonthlyCompilationId == batch.Id && i.Status != IdeaStatus.Shortlisted, ct))
            throw new WorkflowException("Screen all ideas for this month before forwarding.");
        // Set the batch flag only on its first forward. Later addendum forwards must not
        // reset or otherwise change the batch's already-forwarded state.
        if (!isAddendum) batch.ForwardedToBoard = true;
        foreach (var idea in ideasToForward)
        {
            // This is the point at which a shortlisted idea becomes visible to board members.
            idea.Status = IdeaStatus.BoardEvaluation;
            idea.StatusHistory.Add(Event(idea, idea.Status, managerId, isAddendum
                ? $"Forwarded as an addendum to the {year}-{month:00} monthly compilation."
                : $"Forwarded in {year}-{month:00} compilation"));
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Forwarded {IdeaCount} idea(s) from compilation {Year}-{Month:00} as {ForwardType} by manager {ManagerId}.", ideasToForward.Count, year, month, isAddendum ? "addendum" : "regular compilation", managerId);
        return isAddendum;
    }

    public async Task EvaluateAsync(int ideaId, string boardMemberId, int novelty, int feasibility, int relevance, int impact, int viability, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var idea = await db.Ideas.SingleOrDefaultAsync(x => x.Id == ideaId, ct) ?? throw new WorkflowException("Idea not found.");
        if (idea.Status != IdeaStatus.BoardEvaluation) throw new WorkflowException("This idea is not open for board evaluation.");
        var scores = new[] { novelty, feasibility, relevance, impact, viability };
        if (scores.Any(s => !EvaluationRules.IsValidScore(s))) throw new WorkflowException("Each score must be between 1 and 10.");
        var submittedCount = await db.BoardEvaluations.CountAsync(x => x.IdeaId == ideaId, ct);
        if (submittedCount >= EvaluationRules.RequiredBoardMembers) throw new WorkflowException("Three board evaluations have already been received.");
        if (await db.BoardEvaluations.AnyAsync(x => x.IdeaId == ideaId && x.BoardMemberId == boardMemberId, ct)) throw new WorkflowException("You have already submitted an evaluation for this idea.");
        db.BoardEvaluations.Add(new BoardEvaluation { IdeaId = ideaId, BoardMemberId = boardMemberId, Novelty = novelty, Feasibility = feasibility, Relevance = relevance, BusinessImpact = impact, CommercialViability = viability, SubmittedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IdeaCompilation> CompileAsync(int ideaId, string managerId, CancellationToken ct = default)
    {
        var idea = await db.Ideas.Include(x => x.Evaluations).Include(x => x.Result).Include(x => x.Screening).SingleOrDefaultAsync(x => x.Id == ideaId, ct) ?? throw new WorkflowException("Idea not found.");
        if (idea.Screening?.ManagerId != managerId) throw new WorkflowException("This idea is not assigned to your team.");
        if (idea.Status != IdeaStatus.BoardEvaluation || idea.Result is not null) throw new WorkflowException("This idea is not ready for compilation.");
        if (idea.Evaluations.Count != EvaluationRules.RequiredBoardMembers) throw new WorkflowException("Exactly three board evaluations are required before compilation.");
        var average = EvaluationRules.Average(idea.Evaluations);
        var eligible = average >= EvaluationRules.RewardThreshold;
        var result = new IdeaCompilation { IdeaId = idea.Id, EvaluationCount = idea.Evaluations.Count, AverageTotal = average, RewardEligible = eligible, Repository = eligible ? null : RepositoryKind.Repository2, CompiledById = managerId, CompiledAtUtc = DateTime.UtcNow };
        idea.Result = result;
        idea.Status = eligible ? IdeaStatus.Compiled : IdeaStatus.Repository2;
        idea.StatusHistory.Add(Event(idea, idea.Status, managerId, eligible ? "Meets reward consideration threshold; board and manager meeting decision pending" : "Below 40/50 reward consideration threshold"));
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task DecideRewardAsync(int ideaId, string managerId, RewardOutcome outcome, string? notes, CancellationToken ct = default)
    {
        var idea = await db.Ideas.Include(x => x.Result).Include(x => x.Screening).SingleOrDefaultAsync(x => x.Id == ideaId, ct) ?? throw new WorkflowException("Idea not found.");
        if (idea.Screening?.ManagerId != managerId) throw new WorkflowException("This idea is not assigned to your team.");
        if (idea.Result is null || !idea.Result.RewardEligible || idea.Status != IdeaStatus.Compiled) throw new WorkflowException("Only threshold-qualified ideas awaiting the meeting decision can be finalized.");
        if (outcome == RewardOutcome.Pending) throw new WorkflowException("Choose a final meeting outcome.");
        idea.Result.RewardOutcome = outcome;
        idea.Result.MeetingNotes = notes?.Trim();
        // Repository 3 means finally selected for a reward/appreciation. The source does not assign a repository to eligible ideas declined at the meeting.
        if (outcome is RewardOutcome.Rewarded or RewardOutcome.Appreciated)
        {
            idea.Result.Repository = RepositoryKind.Repository3;
            idea.Status = IdeaStatus.Repository3;
        }
        idea.StatusHistory.Add(Event(idea, idea.Status, managerId, $"Meeting outcome: {outcome}"));
        await db.SaveChangesAsync(ct);
    }

    private static IdeaStatusEvent Event(Idea idea, IdeaStatus status, string actor, string? note) => new() { Idea = idea, Status = status, ChangedById = actor, ChangedAtUtc = DateTime.UtcNow, Note = note };
}
