using EducationContentService.Domain.Courses;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

/// <summary>Полное зеркало <see cref="CourseMaterialConfiguration"/> для квизов (#489).</summary>
public class CourseQuizConfiguration : IEntityTypeConfiguration<CourseQuiz>
{
    public void Configure(EntityTypeBuilder<CourseQuiz> builder)
    {
        builder.ToTable("course_quizzes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.CourseId)
            .IsRequired()
            .HasColumnName("course_id");

        builder.Property(x => x.QuizId)
            .IsRequired()
            .HasColumnName("quiz_id");

        builder.Property(x => x.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        builder.HasIndex(x => new { x.CourseId, x.SortKey });
        builder.HasIndex(x => new { x.CourseId, x.QuizId }).IsUnique();
        builder.HasIndex(x => new { x.QuizId, x.CourseId });
    }
}
