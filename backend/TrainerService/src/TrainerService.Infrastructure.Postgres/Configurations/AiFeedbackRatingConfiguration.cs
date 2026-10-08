using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.FeedbackRatings;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class AiFeedbackRatingConfiguration : IEntityTypeConfiguration<AiFeedbackRating>
{
    public void Configure(EntityTypeBuilder<AiFeedbackRating> b)
    {
        b.ToTable("ai_feedback_ratings");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        b.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        b.Property(x => x.SessionItemId).HasColumnName("session_item_id").IsRequired();
        b.Property(x => x.QuestionId).HasColumnName("question_id").IsRequired();

        b.Property(x => x.Rating)
            .HasColumnName("rating")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // One rating per (user, session item) — the upsert/toggle key.
        b.HasIndex(x => new { x.UserId, x.SessionItemId })
            .IsUnique()
            .HasDatabaseName("ux_ai_feedback_ratings_user_item");

        // Per-question admin aggregate: GROUP BY question_id WHERE created_at >= cutoff.
        b.HasIndex(x => x.QuestionId)
            .HasDatabaseName("ix_ai_feedback_ratings_question");

        b.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_ai_feedback_ratings_created_at");
    }
}
