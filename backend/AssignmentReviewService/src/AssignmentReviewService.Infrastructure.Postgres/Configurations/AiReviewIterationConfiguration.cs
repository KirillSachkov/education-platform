using System.Text.Json;
using AssignmentReviewService.Domain.Reviews;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class AiReviewIterationConfiguration : IEntityTypeConfiguration<AiReviewIteration>
{
    public void Configure(EntityTypeBuilder<AiReviewIteration> b)
    {
        b.ToTable("ai_review_iterations");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(x => x.AiReviewId)
            .HasColumnName("ai_review_id")
            .IsRequired();

        b.Property(x => x.IterationNumber)
            .HasColumnName("iteration_number")
            .IsRequired();

        b.Property(x => x.CommitSha)
            .HasColumnName("commit_sha")
            .HasMaxLength(40)
            .IsRequired();

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.Verdict)
            .HasColumnName("verdict")
            .HasConversion<string?>()
            .HasMaxLength(30);

        b.Property(x => x.Summary)
            .HasColumnName("summary")
            .IsRequired();

        b.Property(x => x.InlineCommentsCount)
            .HasColumnName("inline_comments_count")
            .IsRequired();

        b.Property(x => x.GitHubReviewId).HasColumnName("github_review_id");

        b.Property(x => x.ModelUsed)
            .HasColumnName("model_used")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.InputTokens).HasColumnName("input_tokens");
        b.Property(x => x.OutputTokens).HasColumnName("output_tokens");

        b.Property(x => x.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        b.Property(x => x.CompletedAt).HasColumnName("completed_at");

        b.Property(x => x.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(500);

        // #798 — телеметрия цикла дозапроса файлов. jsonb-массив путей, null = дозапроса не было.
        ValueComparer<IReadOnlyList<string>?> requestedFilesComparer = new(
            (a, c) => (a == null && c == null)
                || (a != null && c != null && a.SequenceEqual(c, StringComparer.Ordinal)),
            v => v == null
                ? 0
                : v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode(StringComparison.Ordinal))),
            v => v == null ? null : v.ToList());

        b.Property(x => x.RequestedFiles)
            .HasColumnName("requested_files")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyList<string>?)JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null),
                requestedFilesComparer);

        b.Property(x => x.ContextRounds)
            .HasColumnName("context_rounds")
            .IsRequired();

        // IterationNumber unique per review — prevents race на повторном run.
        b.HasIndex(x => new { x.AiReviewId, x.IterationNumber })
            .IsUnique()
            .HasDatabaseName("uq_ai_review_iterations_review_number");

        b.HasIndex(x => new { x.StartedAt, x.AiReviewId })
            .HasDatabaseName("ix_ai_review_iterations_started_review");
    }
}
