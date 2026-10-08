using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;
using TrainerService.Domain.MockInterviews;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class MockInterviewConfiguration : IEntityTypeConfiguration<MockInterview>
{
    public void Configure(EntityTypeBuilder<MockInterview> b)
    {
        b.ToTable("mock_interviews");

        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnName("id");

        b.Property(m => m.Slug)
            .HasColumnName("slug")
            .HasMaxLength(MockInterview.SLUG_MAX_LENGTH)
            .IsRequired();

        b.Property(m => m.Title)
            .HasColumnName("title")
            .HasMaxLength(MockInterview.TITLE_MAX_LENGTH)
            .IsRequired();

        b.Property(m => m.Description)
            .HasColumnName("description")
            .HasMaxLength(MockInterview.DESCRIPTION_MAX_LENGTH);

        ValueComparer<IReadOnlyList<Guid>> guidListComparer = new(
            (a, c) => (a == null && c == null) || (a != null && c != null && a.SequenceEqual(c)),
            v => v.Aggregate(0, (acc, g) => HashCode.Combine(acc, g.GetHashCode())),
            v => (IReadOnlyList<Guid>)v.ToList());

        b.Property(m => m.TopicIds)
            .HasColumnName("topic_ids")
            .HasColumnType("uuid[]")
            .IsRequired()
            .HasConversion(
                v => v.ToArray(),
                v => (IReadOnlyList<Guid>)v.ToList())
            .Metadata.SetValueComparer(guidListComparer);

        b.Property(m => m.QuestionsPerSession).HasColumnName("questions_per_session");

        b.Property(m => m.SortIndex).HasColumnName("sort_index").IsRequired();

        b.Property(m => m.IsPublished)
            .HasColumnName("is_published")
            .IsRequired();

        b.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Curated questions — own child table (mock_interview_questions). #585.
        b.HasMany(m => m.Questions)
            .WithOne()
            .HasForeignKey("mock_interview_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Field-access: Questions is exposed via `Questions => _questions` getter. Without
        // Field-mode EF reads the navigation read-only and a new item added to private
        // `_questions` is tracked Modified, not Added → UPDATE on 0 rows → concurrency error.
        b.Navigation(m => m.Questions)
            .AutoInclude()
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_questions");

        b.HasIndex(m => m.Slug)
            .IsUnique()
            .HasDatabaseName("ux_mock_interviews_slug");

        b.HasIndex(m => m.SortIndex).HasDatabaseName("ix_mock_interviews_sort_index");
    }
}

internal sealed class MockInterviewQuestionConfiguration : IEntityTypeConfiguration<MockInterviewQuestion>
{
    public void Configure(EntityTypeBuilder<MockInterviewQuestion> b)
    {
        b.ToTable("mock_interview_questions");

        b.HasKey(q => q.Id);

        // EF generates Id via TimeOrderedGuidValueGenerator on Add (nav-collection child).
        // Domain factory leaves Id=Guid.Empty — signals EF the entity is new (Added state),
        // not detached-existing. See docs/agents/backend-transactions.md rule 4.
        b.Property(q => q.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(q => q.QuestionId).HasColumnName("question_id").IsRequired();

        b.Property(q => q.SortIndex).HasColumnName("sort_index").IsRequired();

        b.HasIndex("mock_interview_id", "SortIndex")
            .HasDatabaseName("ix_mock_interview_questions_interview_sort");
    }
}
