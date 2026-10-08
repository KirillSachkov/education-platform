using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class MaterialsIndex
{
    public const string TITLE = "ix_materials_title";
    public const string AUTHOR_ID = "ix_materials_author_id";
    public const string KIND_AUTHOR_ID = "ix_materials_kind_author_id";
    public const string QUIZ_ID = "ix_materials_quiz_id";
}

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials");

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

        builder.Property(x => x.Content)
            .HasConversion(
                v => v!.Value,
                v => MarkdownContent.Create(v).Value)
            .HasMaxLength(MarkdownContent.MAX_LENGTH)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.Description)
            .HasConversion(
                v => v!.Value,
                v => MarkdownContent.Create(v).Value)
            .HasMaxLength(MarkdownContent.MAX_LENGTH)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired(false);

        // Kind ВСЕГДА задаётся в конструкторе Material — DB-level DEFAULT не нужен.
        // Раньше `HasDefaultValue(MaterialKind.ARTICLE)` заставлял EF Core считать,
        // что enum.ARTICLE == default(enum) == «значение не задано», и не отправлять kind в INSERT.
        // Результат: БД проставляла legacy DEFAULT 'Article' (PascalCase), что ломало frontend-валидацию.
        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("kind")
            .IsRequired();

        builder.Property(x => x.AccessType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("access_type")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(x => x.ImageId)
            .HasConversion(
                v => v!.Value,
                v => ImageId.Create(v).Value)
            .IsRequired(false)
            .HasColumnName("image_id");

        builder.Property(x => x.VideoId)
            .HasConversion(
                v => v!.Value,
                v => VideoId.Create(v).Value)
            .IsRequired(false)
            .HasColumnName("video_id");

        builder.Property(x => x.ImageBindingRevision)
            .HasColumnName("image_binding_revision")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(x => x.VideoBindingRevision)
            .HasColumnName("video_binding_revision")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(x => x.MediaVersion)
            .HasColumnName("media_version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        // Ссылка материала на квиз «Проверь себя» (#489). Без FK-constraint'а —
        // консистентно с video_id/image_id; обнуление при удалении квиза делает
        // явный cascade-SQL в DeleteQuizHandler. Один квиз → много материалов
        // (обычный индекс, не unique).
        builder.Property(x => x.QuizId)
            .IsRequired(false)
            .HasColumnName("quiz_id");

        // Денормализованный список заголовков глав — text[] в Postgres.
        // Backing: List<string>; раньше raise'ился через Material.UpdateChapterTitles,
        // теперь — через Material.UpdateChapters(titles, timestamps).
        builder.Property(x => x.ChapterTitles)
            .HasColumnName("chapter_titles")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired()
            .Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<string>>(
                    (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode(StringComparison.Ordinal))),
                    v => v.ToArray()));

        // Параллельный массив offset'ов глав в секундах — integer[] в Postgres.
        // chapter_titles[i] ↔ chapter_timestamps[i]. Длины совпадают (валидация в handler'е).
        builder.Property(x => x.ChapterTimestamps)
            .HasColumnName("chapter_timestamps")
            .HasColumnType("integer[]")
            .HasDefaultValueSql("'{}'::integer[]")
            .IsRequired()
            .Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<int>>(
                    (a, b) => (a ?? Array.Empty<int>()).SequenceEqual(b ?? Array.Empty<int>()),
                    v => v.Aggregate(0, HashCode.Combine),
                    v => v.ToArray()));

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.Property(x => x.PublishedAt)
            .HasColumnName("published_at")
            .IsRequired(false);

        builder.HasIndex(x => x.Title)
            .IsUnique()
            .HasFilter("status IN ('PUBLISHED', 'ARCHIVED')")
            .HasDatabaseName(MaterialsIndex.TITLE);

        builder.HasIndex(x => x.AuthorId)
            .HasDatabaseName(MaterialsIndex.AUTHOR_ID);

        builder.HasIndex(x => new { x.Kind, x.AuthorId })
            .HasDatabaseName(MaterialsIndex.KIND_AUTHOR_ID);

        builder.HasIndex(x => x.QuizId)
            .HasDatabaseName(MaterialsIndex.QUIZ_ID);
    }
}
