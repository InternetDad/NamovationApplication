using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Namovation.Domain;

namespace Namovation.Infrastructure.Data;

public sealed class NamovationDbContext(DbContextOptions<NamovationDbContext> options)
    : IdentityDbContext<NamovationUser>(options)

{
    public DbSet<Idea> Ideas => Set<Idea>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<MonthlyCompilation> MonthlyCompilations => Set<MonthlyCompilation>();
    public DbSet<Screening> Screenings => Set<Screening>();
    public DbSet<BoardEvaluation> BoardEvaluations => Set<BoardEvaluation>();
    public DbSet<IdeaCompilation> IdeaCompilations => Set<IdeaCompilation>();
    public DbSet<IdeaStatusEvent> IdeaStatusEvents => Set<IdeaStatusEvent>();
    public DbSet<IdeaAttachment> IdeaAttachments => Set<IdeaAttachment>();
    public DbSet<RegistrationSettings> RegistrationSettings => Set<RegistrationSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Department>().Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Entity<Department>().Property(x => x.NormalizedName).HasMaxLength(120).IsRequired();
        b.Entity<Department>().HasIndex(x => x.NormalizedName).IsUnique();
        b.Entity<Department>().HasIndex(x => x.ManagerId);
        b.Entity<Department>().HasOne<NamovationUser>().WithMany().HasForeignKey(x => x.ManagerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Departments_AspNetUsers_ManagerId");
        b.Entity<Idea>().HasIndex(x => x.ReferenceNumber).IsUnique();
        b.Entity<Idea>().Property(x => x.Title).HasMaxLength(180).IsRequired();
        b.Entity<Idea>().Property(x => x.Description).HasMaxLength(6000).IsRequired();
        b.Entity<Idea>().HasIndex(x => new { x.SubmitterId, x.SubmittedAtUtc });
        b.Entity<Idea>().HasIndex(x => x.DepartmentId);
        b.Entity<Idea>().HasOne(x => x.Department).WithMany(x => x.Ideas).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<MonthlyCompilation>().HasIndex(x => new { x.Year, x.Month, x.CreatedById }).IsUnique();
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
        b.Entity<Idea>().HasOne(x => x.Attachment).WithOne(x => x.Idea).HasForeignKey<IdeaAttachment>(x => x.IdeaId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<IdeaAttachment>().Property(x => x.SizeBytes).HasColumnType("bigint");
        b.Entity<IdeaAttachment>().HasIndex(x => x.StoredFileName).IsUnique();
        b.Entity<IdeaAttachment>().Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        b.Entity<IdeaAttachment>().Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        b.Entity<IdeaAttachment>().Property(x => x.StoredFileName).HasMaxLength(80).IsRequired();
        b.Entity<MonthlyCompilation>().HasMany(x => x.Ideas).WithOne(x => x.MonthlyCompilation).HasForeignKey(x => x.MonthlyCompilationId);
        // Deliberately omit SubmitterId and identity profile data from all board-facing projections.
    }
}
