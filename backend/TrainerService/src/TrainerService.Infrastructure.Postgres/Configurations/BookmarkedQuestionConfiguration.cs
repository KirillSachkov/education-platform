using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.Bookmarks;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class BookmarkedQuestionConfiguration : IEntityTypeConfiguration<BookmarkedQuestion>
{
    public void Configure(EntityTypeBuilder<BookmarkedQuestion> b)
    {
        b.ToTable("bookmarked_questions");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        b.Property(x => x.TopicId).HasColumnName("topic_id");
        b.Property(x => x.QuestionId).HasColumnName("question_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => new { x.UserId, x.QuestionId })
            .IsUnique()
            .HasDatabaseName("ux_bookmarked_questions_user_question");

        b.HasIndex(x => new { x.UserId, x.TopicId })
            .HasDatabaseName("ix_bookmarked_questions_user_topic");
    }
}