using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class CoursesIndex
{
    public const string TITLE = "ix_courses_title";
    public const string AUTHOR_ID = "ix_courses_author_id";
    public const string SLUG = "ix_courses_slug_unique";
    public const string AUTHOR_SORT_KEY = "ix_courses_author_id_sort_key";
}

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("courses");

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

        builder.Property(x => x.Description)
            .HasConversion(
                v => v.Value,
                v => Description.Create(v).Value)
            .HasMaxLength(Description.MAX_LENGTH)
            .HasColumnName("description")
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

        builder.Property(x => x.AssetOwnershipRevision)
            .HasColumnName("asset_ownership_revision")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();
        
        builder.Property(x => x.GettingStartedModuleId)
            .IsRequired(false)
            .HasColumnName("getting_started_module_id");

        builder.Property(x => x.Slug)
            .HasColumnName("slug")
            .HasMaxLength(CourseSlug.MAX_LENGTH)
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => CourseSlug.Create(v).Value);

        builder.Property(x => x.IsNew)
            .HasColumnName("is_new")
            .HasDefaultValue(false)
            .IsRequired();

        // Тип курса: 'COURSE' (полноценный) / 'INTENSIVE' (мини-курс без issues).
        // Без HasDefaultValue — Domain-ctor задаёт значение (см. правило в backend/CLAUDE.md
        // про enum-storage). Default на БД-уровне выставлен в миграции (`AddCourseKind`)
        // только для backfill'а существующих строк; для INSERT'ов EF Core всегда пишет
        // значение из ctor'а.
        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("kind")
            .IsRequired();

        // Display-флаг для showcase «Полный доступ» на /pricing. БЕЗ HasDefaultValue —
        // Domain-ctor задаёт значение (default = true). DB-level default true выставлен
        // в миграции (AddCourseShowInFullAccess) только для backfill'а существующих строк.
        builder.Property(x => x.ShowInFullAccess)
            .HasColumnName("show_in_full_access")
            .IsRequired();

        // Catalog-visibility gate (#569). БЕЗ HasDefaultValue / ValueGeneratedOnAdd —
        // Domain-ctor ВСЕГДА задаёт значение (true для админа, false для обычного автора).
        // Если бы EF считал колонку store-generated, INSERT не-админского курса
        // (listed=false=default(bool)) пропустил бы колонку → БД-DEFAULT true ошибочно
        // победил бы. DB-level default true выставлен в миграции (AddCourseIsCatalogListed)
        // ТОЛЬКО для backfill'а существующих строк — для INSERT'ов EF пишет значение ctor'а.
        builder.Property(x => x.IsCatalogListed)
            .HasColumnName("is_catalog_listed")
            .IsRequired();

        builder.Property(x => x.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.Property(x => x.PublishedAt)
            .IsRequired(false)
            .HasColumnName("published_at");

        // Author landing copy — денорм text[] в Postgres (паттерн Material.ChapterTitles).
        // «Чему вы научитесь» / «Для кого» / «Что нужно знать заранее».
        builder.Property(x => x.LearningOutcomes)
            .HasColumnName("learning_outcomes")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired()
            .Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<string>>(
                    (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode(StringComparison.Ordinal))),
                    v => v.ToArray()));

        builder.Property(x => x.TargetAudience)
            .HasColumnName("target_audience")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired()
            .Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<string>>(
                    (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode(StringComparison.Ordinal))),
                    v => v.ToArray()));

        builder.Property(x => x.Prerequisites)
            .HasColumnName("prerequisites")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired()
            .Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<string>>(
                    (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode(StringComparison.Ordinal))),
                    v => v.ToArray()));

        builder.HasMany<CourseItem>()
            .WithOne()
            .HasForeignKey(ci => ci.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany<CourseMaterial>()
            .WithOne()
            .HasForeignKey(cm => cm.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Title).IsUnique().HasFilter("status <> 'DRAFT'").HasDatabaseName(CoursesIndex.TITLE);
        builder.HasIndex(x => x.AuthorId).HasDatabaseName(CoursesIndex.AUTHOR_ID);
        builder.HasIndex(x => x.Slug).IsUnique().HasDatabaseName(CoursesIndex.SLUG);
        builder.HasIndex(x => new { x.AuthorId, x.SortKey }).HasDatabaseName(CoursesIndex.AUTHOR_SORT_KEY);
    }
}
