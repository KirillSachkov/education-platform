using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Issues;
using ProgressService.Domain.IssueSubmissions;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class IssueSubmissionConfiguration : IEntityTypeConfiguration<IssueSubmission>
{
    public const string ISSUE_PROGRESS_ATTEMPT_INDEX = "ux_issue_submissions_issue_progress_id_attempt_number";
    public const string REVIEW_STATUS_SUBMITTED_AT_INDEX = "ix_issue_submissions_review_status_submitted_at";
    public const string REVIEW_STATUS_REVIEWED_AT_INDEX = "ix_issue_submissions_review_status_reviewed_at";
    public const string READY_FOR_REVIEW_SUBMITTED_AT_INDEX = "ix_issue_submissions_ready_for_review_submitted_at";
    public const string ISSUE_PROGRESS_FOREIGN_KEY = "fk_issue_submissions_issue_progress_issue_progress_id";

    public void Configure(EntityTypeBuilder<IssueSubmission> builder)
    {
        builder.ToTable("issue_submissions");

        builder.HasKey(x => x.Id);

        builder.HasOne<IssueProgress>()
            .WithMany()
            .HasForeignKey(x => x.IssueProgressId)
            .HasConstraintName(ISSUE_PROGRESS_FOREIGN_KEY)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.Version)
            .IsRowVersion();

        builder.Property(x => x.IssueProgressId)
            .IsRequired()
            .HasColumnName("issue_progress_id");

        builder.Property(x => x.AttemptNumber)
            .IsRequired()
            .HasConversion(
                attemptNumber => attemptNumber.Value,
                value => AttemptNumber.Create(value).Value)
            .HasColumnName("attempt_number");

        builder.Property(x => x.Payload)
            .IsRequired()
            .HasConversion(
                payload => payload.Value,
                value => IssueSubmissionPayload.Restore(value).Value)
            .HasMaxLength(IssueSubmissionPayload.MaxLength)
            .HasColumnName("payload");

        builder.Property(x => x.ReviewStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("review_status");

        builder.Property(x => x.ReviewerId)
            .HasColumnName("reviewer_id");

        builder.Property(x => x.ReviewStartedAt)
            .HasColumnName("review_started_at");

        builder.Property(x => x.Feedback)
            .HasConversion(
                feedback => feedback == null ? null : feedback.Value,
                value => value == null ? null : IssueReviewFeedback.Create(value).Value)
            .HasMaxLength(IssueReviewFeedback.MAX_LENGTH)
            .HasColumnName("feedback");

        builder.Property(x => x.SubmittedAt)
            .IsRequired()
            .HasColumnName("submitted_at");

        builder.Property(x => x.ReviewedAt)
            .HasColumnName("reviewed_at");

        // Phase 8 (#15) — AI denorm fields. Все nullable / default-safe; existing
        // submissions backfilled migration'ом до true (ReadyForHumanReview).
        builder.Property(x => x.LatestAiVerdict)
            .HasColumnName("latest_ai_verdict")
            .HasMaxLength(30);

        builder.Property(x => x.AiIterationsCount)
            .HasColumnName("ai_iterations_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.LastAiIterationAt)
            .HasColumnName("last_ai_iteration_at");

        builder.Property(x => x.AiReviewStatus)
            .HasColumnName("ai_review_status")
            .HasMaxLength(20);

        builder.Property(x => x.ReadyForHumanReview)
            .HasColumnName("ready_for_human_review")
            .HasDefaultValue(true)
            .IsRequired();

        // #383 «Позвать автора» — nullable timestamp, без HasDefaultValue (не enum).
        builder.Property(x => x.AuthorHelpRequestedAt)
            .HasColumnName("author_help_requested_at");

        // #575 — свободный текст «в чём нужна помощь». Необязательная колонка, без БД-дефолта.
        builder.Property(x => x.AuthorHelpMessage)
            .HasColumnName("author_help_message")
            .HasMaxLength(IssueSubmission.AUTHOR_HELP_MESSAGE_MAX_LENGTH);

        // #713 «вопрос студента в PR» — nullable timestamp последнего вопроса, без HasDefaultValue.
        builder.Property(x => x.StudentQuestionAt)
            .HasColumnName("student_question_at");

        builder.HasIndex(x => new { x.IssueProgressId, x.AttemptNumber })
            .HasDatabaseName(ISSUE_PROGRESS_ATTEMPT_INDEX)
            .IsUnique();

        builder.HasIndex(x => new { x.ReviewStatus, x.SubmittedAt })
            .HasDatabaseName(REVIEW_STATUS_SUBMITTED_AT_INDEX);

        builder.HasIndex(x => new { x.ReviewStatus, x.ReviewedAt })
            .HasDatabaseName(REVIEW_STATUS_REVIEWED_AT_INDEX);

        // Phase 8 (#15) — author-inbox query фильтрует по review_status IN
        // ('PENDING','IN_REVIEW') AND ready_for_human_review=true → composite
        // index на (review_status, ready_for_human_review, submitted_at desc).
        builder.HasIndex(x => new { x.ReviewStatus, x.ReadyForHumanReview, x.SubmittedAt })
            .HasDatabaseName(READY_FOR_REVIEW_SUBMITTED_AT_INDEX);
    }
}
