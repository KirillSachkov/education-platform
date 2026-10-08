using AssignmentReviewService.Domain.Reviews;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class AiReviewConfiguration : IEntityTypeConfiguration<AiReview>
{
    public void Configure(EntityTypeBuilder<AiReview> b)
    {
        b.ToTable("ai_reviews");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(x => x.SubmissionId)
            .HasColumnName("submission_id")
            .IsRequired();

        b.Property(x => x.IssueId)
            .HasColumnName("issue_id")
            .IsRequired();

        b.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        b.Property(x => x.AuthorId)
            .HasColumnName("author_id")
            .IsRequired()
            .HasDefaultValue(Guid.Empty);

        b.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.RepoFullName)
            .HasColumnName("repo_full_name")
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.PullNumber)
            .HasColumnName("pull_number")
            .IsRequired();

        b.Property(x => x.PullRequestUrl)
            .HasColumnName("pull_request_url")
            .HasMaxLength(500)
            .IsRequired();

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.LatestIterationId).HasColumnName("latest_iteration_id");

        b.Property(x => x.IterationsCount)
            .HasColumnName("iterations_count")
            .IsRequired();

        b.Property(x => x.LatestVerdict)
            .HasColumnName("latest_verdict")
            .HasConversion<string?>()
            .HasMaxLength(30);

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // #690 — liveness heartbeat для длинных RUNNING-ревью. Nullable: до первого
        // batch-heartbeat'а отсутствует, watchdog тогда смотрит на updated_at.
        b.Property(x => x.HeartbeatAt)
            .HasColumnName("heartbeat_at");

        // 1 AiReview per submission. Идемпотентность создания на event
        // issue_submission.created.
        b.HasIndex(x => x.SubmissionId)
            .IsUnique()
            .HasDatabaseName("uq_ai_reviews_submission");

        b.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_ai_reviews_user_id");

        b.HasIndex(x => x.IssueId)
            .HasDatabaseName("ix_ai_reviews_issue_id");

        b.HasIndex(x => x.AuthorId)
            .HasDatabaseName("ix_ai_reviews_author_id");

        b.HasMany(x => x.Iterations)
            .WithOne()
            .HasForeignKey(i => i.AiReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        // Field-access — EF читает navigation через приватный backing field
        // `_iterations`. Без этого новый AiReviewIteration попадает в Change
        // Tracker как Modified, не Added (см. docs/agents/backend-transactions.md
        // правило #4 + AccessService PlanOnboardingFlowConfiguration).
        b.Navigation(x => x.Iterations)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_iterations");
    }
}
