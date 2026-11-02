using System.ComponentModel.DataAnnotations;
using Namovation.Domain;

namespace Namovation.Web.Models;

public sealed class IdeaInput
{
    [Required, StringLength(180)] public string Title { get; set; } = "";
    [Required, StringLength(6000, MinimumLength = 20)] public string Description { get; set; } = "";
    [StringLength(2000)] public string? ProblemStatement { get; set; }
    [StringLength(2000)] public string? ProposedBenefit { get; set; }
}
public sealed class ScreenInput { public int Id { get; set; } public bool Shortlist { get; set; } public string? Rationale { get; set; } public string? BoardTitle { get; set; } public string? BoardDescription { get; set; } }
public sealed class ScoreInput
{
    public int IdeaId { get; set; }
    [Range(1,10)] public int Novelty { get; set; } = 5;
    [Range(1,10)] public int Feasibility { get; set; } = 5;
    [Range(1,10)] public int Relevance { get; set; } = 5;
    [Range(1,10)] public int BusinessImpact { get; set; } = 5;
    [Range(1,10)] public int CommercialViability { get; set; } = 5;
}
public sealed record IdeaRow(int Id, string ReferenceNumber, string Title, string? Description, IdeaStatus Status, DateTime SubmittedAt, string? Submitter, int EvaluationCount, decimal? AverageTotal, bool? RewardEligible, RewardOutcome? RewardOutcome, RepositoryKind? Repository, string? ProblemStatement = null, string? ProposedBenefit = null);
public sealed record ScoreLine(string BoardMember, int Novelty, int Feasibility, int Relevance, int BusinessImpact, int CommercialViability)
{ public int Total => Novelty + Feasibility + Relevance + BusinessImpact + CommercialViability; }
public sealed record CompilationReview(IdeaRow Idea, IReadOnlyList<ScoreLine> Scores);
public sealed record BoardIdeaRow(int Id, string ReferenceNumber, string Title, string Description, bool Evaluated);
public sealed record ManagerPage(IReadOnlyList<IdeaRow> Pending, IReadOnlyList<IdeaRow> ReadyToCompile, IReadOnlyList<MonthlyCompilation> Months, IReadOnlyList<IdeaRow> Repositories);
