using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class QuizzesIndex
{
    public const string TITLE = "ix_quizzes_title";
    public const string AUTHOR_ID = "ix_quizzes_author_id";
}

public class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> builder)
    {
        builder.ToTable("quizzes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .IsRequired()
            .HasColumnName("id");

        builder.Property(x => x.AuthorId)
            .IsRequired()
            .HasColumnName("author_id");

        builder.Property(x => x.Title)
            .HasConversion(
                v => v.Value,
                v => Title.Create(v).Value)
            .HasMaxLength(Title.MAX_LENGTH)
            .HasColumnName("title")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status")
            .IsRequired();

        // Без HasDefaultValue: 0 — валидное значение (см. enum-gotcha в backend/CLAUDE.md —
        // дефолт ставит конструктор агрегата, иначе EF пропускает колонку в INSERT).
        builder.Property(x => x.PassingScorePercent)
            .HasColumnName("passing_score_percent")
            .IsRequired();

        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("purpose")
            .IsRequired();

        // Уровень доступа квиза — зеркало Material.AccessType. Без HasDefaultValue —
        // значение задаёт domain factory (см. enum-gotcha в backend/CLAUDE.md). DB-default
        // 'PUBLIC' выставлен в миграции (InvertQuizMaterialLink) только для backfill'а
        // существующих строк.
        builder.Property(x => x.AccessType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("access_type")
            .IsRequired();

        // JSONB-массив вопросов через явный value converter (НЕ OwnsMany+ToJson: у
        // JSON-owned сущностей Id становится ключом и не round-trip'ится — см.
        // QuizQuestionsJson). Порядок элементов массива = порядок вопросов.
        builder.Property(x => x.Questions)
            .HasConversion(QuizQuestionsJson.Converter, QuizQuestionsJson.Comparer)
            .HasColumnType("jsonb")
            .HasColumnName("questions")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.HasIndex(x => x.Title).IsUnique().HasFilter("status <> 'DRAFT'").HasDatabaseName(QuizzesIndex.TITLE);
        builder.HasIndex(x => x.AuthorId).HasDatabaseName(QuizzesIndex.AUTHOR_ID);
    }
}