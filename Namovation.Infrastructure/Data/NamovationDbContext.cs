using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Namovation.Domain;

namespace Namovation.Infrastructure.Data;

public sealed class NamovationDbContext(DbContextOptions<NamovationDbContext> options)
    : IdentityDbContext<NamovationUser>(options)

{
    public DbSet<Idea> Ideas => Set<Idea>();
    public DbSet<MonthlyCompilation> MonthlyCompilations => Set<MonthlyCompilation>();
    public DbSet<Screening> Screenings => Set<Screening>();
    public DbSet<BoardEvaluation> BoardEvaluations => Set<BoardEvaluation>();
    public DbSet<IdeaCompilation> IdeaCompilations => Set<IdeaCompilation>();
    public DbSet<IdeaStatusEvent> IdeaStatusEvents => Set<IdeaStatusEvent>();
    public DbSet<RegistrationSettings> RegistrationSettings => Set<RegistrationSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Idea>().HasIndex(x => x.ReferenceNumber).IsUnique();
        b.Entity<Idea>().Property(x => x.Title).HasMaxLength(180).IsRequired();
        b.Entity<Idea>().Property(x => x.Description).HasMaxLength(6000).IsRequired();
        b.Entity<Idea>().HasIndex(x => new { x.SubmitterId, x.SubmittedAtUtc });
        b.Entity<MonthlyCompilation>().HasIndex(x => new { x.Year, x.Month }).IsUnique();
        b.Entity<Screening>().HasIndex(x => x.IdeaId).IsUnique();
        b.Entity<BoardEvaluation>().HasIndex(x => new { x.IdeaId, x.BoardMemberId }).IsUnique();
        b.Entity<IdeaCompilation>().HasIndex(x => x.IdeaId).IsUnique();
        b.Entity<IdeaCompilation>().Property(x => x.AverageTotal).HasPrecision(5, 2);
        b.Entity<RegistrationSettings>().Property(x => x.AuthenticatorSetupRequired).HasDefaultValue(true);
        b.Entity<RegistrationSettings>().ToTable(t => t.HasCheckConstraint("CK_RegistrationSettings_SingletonId", "\"Id\" = 1"));
        b.Entity<Idea>().HasOne(x => x.Result).WithOne(x => x.Idea).HasForeignKey<IdeaCompilation>(x => x.IdeaId);
        b.Entity<Idea>().HasOne(x => x.Screening).WithOne(x => x.Idea).HasForeignKey<Screening>(x => x.IdeaId);
        b.Entity<Idea>().HasMany(x => x.Evaluations).WithOne(x => x.Idea).HasForeignKey(x => x.IdeaId);
        b.Entity<Idea>().HasMany(x => x.StatusHistory).WithOne(x => x.Idea).HasForeignKey(x => x.IdeaId);
        b.Entity<MonthlyCompilation>().HasMany(x => x.Ideas).WithOne(x => x.MonthlyCompilation).HasForeignKey(x => x.MonthlyCompilationId);
        // Deliberately omit SubmitterId and identity profile data from all board-facing projections.
    }
}
