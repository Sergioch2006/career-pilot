using CareerPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerPilot.Api.Data;

/// <summary>
/// Contexto EF Core para o banco SQLite local (careerpilot.db).
/// </summary>
public class CareerPilotDbContext : DbContext
{
    public CareerPilotDbContext(DbContextOptions<CareerPilotDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<ProfileQA> ProfileQAs => Set<ProfileQA>();
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<AiExecutionLog> AiExecutionLogs => Set<AiExecutionLog>();
    public DbSet<ProjectMilestone> ProjectMilestones => Set<ProjectMilestone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("UserProfile");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.SourceType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SourceFileName).HasMaxLength(255);
            entity.Property(e => e.RawText).IsRequired();
        });

        modelBuilder.Entity<ProfileQA>(entity =>
        {
            entity.ToTable("ProfileQA");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Question).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(100);

            entity.HasOne(e => e.UserProfile)
                .WithMany(p => p.QuestionsAndAnswers)
                .HasForeignKey(e => e.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobOffer>(entity =>
        {
            entity.ToTable("JobOffers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Company).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Url).HasMaxLength(500);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.FitScore).HasPrecision(5, 2);
            entity.Property(e => e.AnalysisStatus).IsRequired().HasMaxLength(30).HasDefaultValue("UpToDate");

            entity.HasOne<UserProfile>()
                .WithMany()
                .HasForeignKey(e => e.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiExecutionLog>(entity =>
        {
            entity.ToTable("AiExecutionLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Operation).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Model).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EstimatedCostUsd).HasPrecision(12, 6);
        });

        modelBuilder.Entity<ProjectMilestone>(entity =>
        {
            entity.ToTable("ProjectMilestones");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Tags).HasMaxLength(300);
        });
    }
}
