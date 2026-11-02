using Namovation.Domain;

namespace Namovation.Application;

public interface IWorkflowService
{
    Task<Idea> SubmitIdeaAsync(string employeeId, string title, string description, string? problem, string? benefit, CancellationToken ct = default);
    Task ScreenAsync(int ideaId, string managerId, bool shortlist, string? rationale, string? boardTitle, string? boardDescription, CancellationToken ct = default);
    Task ForwardMonthAsync(int year, int month, string managerId, CancellationToken ct = default);
    Task EvaluateAsync(int ideaId, string boardMemberId, int novelty, int feasibility, int relevance, int impact, int viability, CancellationToken ct = default);
    Task<IdeaCompilation> CompileAsync(int ideaId, string managerId, CancellationToken ct = default);
    Task DecideRewardAsync(int ideaId, string managerId, RewardOutcome outcome, string? notes, CancellationToken ct = default);
}

public sealed class WorkflowException(string message) : Exception(message);
