using Namovation.Domain;

namespace Namovation.Application;

public interface IWorkflowService
{
    Task<Idea> SubmitIdeaAsync(string employeeId, int departmentId, string title, string description, string? problem, string? benefit, SupportingDocumentUpload? attachment = null, CancellationToken ct = default);
    Task<bool> ScreenAsync(int ideaId, string managerId, bool shortlist, string? rationale, string? boardDescription, CancellationToken ct = default);
    Task<int> RequeueUnreviewedAutoAddendaAsync(string managerId, CancellationToken ct = default);
    Task<bool> ForwardMonthAsync(int year, int month, string managerId, CancellationToken ct = default);
    Task EvaluateAsync(int ideaId, string boardMemberId, int novelty, int feasibility, int relevance, int impact, int viability, CancellationToken ct = default);
    Task<IdeaCompilation> CompileAsync(int ideaId, string managerId, CancellationToken ct = default);
    Task DecideRewardAsync(int ideaId, string managerId, RewardOutcome outcome, string? notes, CancellationToken ct = default);
}

public sealed class WorkflowException(string message) : Exception(message);
