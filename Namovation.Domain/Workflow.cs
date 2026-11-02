namespace Namovation.Domain;

public enum IdeaStatus { Submitted, Shortlisted, BoardEvaluation, Compiled, Repository1, Repository2, Repository3 }
public enum RepositoryKind { Repository1 = 1, Repository2 = 2, Repository3 = 3 }
public enum RewardOutcome { Pending, Rewarded, Appreciated, NotSelected }

public sealed class Idea
{
    public int Id { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ProblemStatement { get; set; }
    public string? ProposedBenefit { get; set; }
    public string SubmitterId { get; set; } = "";
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public IdeaStatus Status { get; set; } = IdeaStatus.Submitted;
    public int? MonthlyCompilationId { get; set; }
    public MonthlyCompilation? MonthlyCompilation { get; set; }
    public Screening? Screening { get; set; }
    public List<BoardEvaluation> Evaluations { get; set; } = [];
    public IdeaCompilation? Result { get; set; }
    public List<IdeaStatusEvent> StatusHistory { get; set; } = [];
}

public sealed class MonthlyCompilation
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedById { get; set; } = "";
    public bool ForwardedToBoard { get; set; }
    public List<Idea> Ideas { get; set; } = [];
}

public sealed class Screening
{
    public int Id { get; set; }
    public int IdeaId { get; set; }
    public Idea Idea { get; set; } = null!;
    public string ManagerId { get; set; } = "";
    public bool Shortlisted { get; set; }
    public string? Rationale { get; set; }
    public string? BoardFacingTitle { get; set; }
    public string? BoardFacingDescription { get; set; }
    public DateTime ScreenedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BoardEvaluation
{
    public int Id { get; set; }
    public int IdeaId { get; set; }
    public Idea Idea { get; set; } = null!;
    public string BoardMemberId { get; set; } = "";
    public int Novelty { get; set; }
    public int Feasibility { get; set; }
    public int Relevance { get; set; }
    public int BusinessImpact { get; set; }
    public int CommercialViability { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public int Total => Novelty + Feasibility + Relevance + BusinessImpact + CommercialViability;
}

public sealed class IdeaCompilation
{
    public int Id { get; set; }
    public int IdeaId { get; set; }
    public Idea Idea { get; set; } = null!;
    public int EvaluationCount { get; set; }
    public decimal AverageTotal { get; set; }
    public bool RewardEligible { get; set; }
    public RepositoryKind? Repository { get; set; }
    public RewardOutcome RewardOutcome { get; set; } = RewardOutcome.Pending;
    public string? MeetingNotes { get; set; }
    public DateTime CompiledAtUtc { get; set; } = DateTime.UtcNow;
    public string CompiledById { get; set; } = "";
}

public sealed class IdeaStatusEvent
{
    public int Id { get; set; }
    public int IdeaId { get; set; }
    public Idea Idea { get; set; } = null!;
    public IdeaStatus Status { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public string ChangedById { get; set; } = "system";
    public string? Note { get; set; }
}

public static class EvaluationRules
{
    public const int RequiredBoardMembers = 3;
    public const int RewardThreshold = 40;
    public const int MaximumPerEvaluation = 50;
    public static bool IsValidScore(int value) => value is >= 1 and <= 10;
    public static decimal Average(IEnumerable<BoardEvaluation> evaluations) => (decimal)evaluations.Average(e => e.Total);
}
