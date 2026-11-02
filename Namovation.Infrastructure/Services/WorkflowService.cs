using Microsoft.EntityFrameworkCore;
using System.Data;
using Namovation.Application;
using Namovation.Domain;
using Namovation.Infrastructure.Data;

namespace Namovation.Infrastructure.Services;

public sealed class WorkflowService(NamovationDbContext db) : IWorkflowService
{
    public async Task<Idea> SubmitIdeaAsync(string employeeId, string title, string description, string? problem, string? benefit, CancellationToken ct = default)
    {
        var idea = new Idea {
            ReferenceNumber = $"N-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..15].ToUpperInvariant(),
            SubmitterId = employeeId, Title = title.Trim(), Description = description.Trim(),
            ProblemStatement = problem?.Trim(), ProposedBenefit = benefit?.Trim(), SubmittedAtUtc = DateTime.UtcNow,
            Status = IdeaStatus.Submitted
        };
        idea.StatusHistory.Add(Event(idea, idea.Status, employeeId, "Idea submitted"));
        db.Ideas.Add(idea);
        await db.SaveChangesAsync(ct);
        return idea;
    }

    public async Task ScreenAsync(int ideaId, string managerId, bool shortlist, string? rationale, string? boardTitle, string? boardDescription, CancellationToken ct = default)
    {
        var idea = await db.Ideas.Include(x => x.Screening).Include(x => x.MonthlyCompilation)
            .SingleOrDefaultAsync(x => x.Id == ideaId, ct) ?? throw new WorkflowException("Idea not found.");
        if (idea.Status != IdeaStatus.Submitted) throw new WorkflowException("Only newly submitted ideas can be screened.");
        idea.Screening = new Screening { ManagerId = managerId, Shortlisted = shortlist, Rationale = rationale?.Trim(), ScreenedAtUtc = DateTime.UtcNow };
        if (shortlist)
        {
            if (string.IsNullOrWhiteSpace(boardTitle) || string.IsNullOrWhiteSpace(boardDescription)) throw new WorkflowException("Provide the anonymized title and description that the board will review.");
            idea.Screening.BoardFacingTitle = boardTitle.Trim();
            idea.Screening.BoardFacingDescription = boardDescription.Trim();
            var month = idea.SubmittedAtUtc;
            var batch = await db.MonthlyCompilations.SingleOrDefaultAsync(x => x.Year == month.Year && x.Month == month.Month, ct);
            if (batch is null) { batch = new MonthlyCompilation { Year = month.Year, Month = month.Month, CreatedById = managerId }; db.MonthlyCompilations.Add(batch); }
            idea.MonthlyCompilation = batch;
            idea.Status = IdeaStatus.Shortlisted;
        }
        else
        {
            idea.Status = IdeaStatus.Repository1;
            idea.Result = new IdeaCompilation { EvaluationCount = 0, AverageTotal = 0, RewardEligible = false, Repository = RepositoryKind.Repository1, CompiledById = managerId };
        }
        idea.StatusHistory.Add(Event(idea, idea.Status, managerId, rationale));
        await db.SaveChangesAsync(ct);
    }

    public async Task ForwardMonthAsync(int year, int month, string managerId, CancellationToken ct = default)
    {
        if (month is < 1 or > 12) throw new WorkflowException("Choose a valid month.");
        var batch = await db.MonthlyCompilations.Include(x => x.Ideas).ThenInclude(x => x.Screening)
            .SingleOrDefaultAsync(x => x.Year == year && x.Month == month, ct) ?? throw new WorkflowException("No screened ideas are ready for this month.");
        if (batch.ForwardedToBoard) throw new WorkflowException("This monthly compilation has already been forwarded.");
        if (batch.Ideas.Count == 0 || batch.Ideas.Any(i => i.Status != IdeaStatus.Shortlisted)) throw new WorkflowException("Screen all ideas for this month before forwarding.");
        batch.ForwardedToBoard = true;
        foreach (var idea in batch.Ideas) { idea.Status = IdeaStatus.BoardEvaluation; idea.StatusHistory.Add(Event(idea, idea.Status, managerId, $"Forwarded in {year}-{month:00} compilation")); }
        await db.SaveChangesAsync(ct);
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
        var idea = await db.Ideas.Include(x => x.Evaluations).Include(x => x.Result).SingleOrDefaultAsync(x => x.Id == ideaId, ct) ?? throw new WorkflowException("Idea not found.");
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
        var idea = await db.Ideas.Include(x => x.Result).SingleOrDefaultAsync(x => x.Id == ideaId, ct) ?? throw new WorkflowException("Idea not found.");
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
