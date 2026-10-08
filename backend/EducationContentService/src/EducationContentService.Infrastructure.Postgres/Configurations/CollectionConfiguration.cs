using EducationContentService.Domain.Collections;
using EducationContentService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class CollectionsIndex
{
    public const string AUTHOR_ID = "ix_collections_author_id";
    public const string COURSE_ID = "ix_collections_course_id";
    public const string AUTHOR_STATUS = "ix_collections_author_status";
    public const string PINNED = "ix_collections_pinned";
}

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("collections");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
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
                v => v!.Value,
                v => Description.Create(v).Value)
            .HasMaxLength(Description.MAX_LENGTH)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.CoverImageId)
            .HasColumnName("cover_image_id")
            .IsRequired(false);

        builder.Property(x => x.CoverBindingRevision)
            .HasColumnName("cover_binding_revision")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(x => x.MediaVersion)
            .HasColumnName("media_version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(x => x.CourseId)
            .HasColumnName("course_id")
            .IsRequired(false);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status")
            .IsRequired();

        // AccessType сознательно без HasDefaultValue — значение ставит конструктор aggregate.
        // См. backend/CLAUDE.md → «Enum Storage Convention» про ловушки с default в БД.
        builder.Property(x => x.AccessType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("access_type")
            .IsRequired();

        builder.Property(x => x.IsPinned)
            .HasColumnName("is_pinned")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.PinnedSortKey)
            .HasConversion(
                v => v!.Value,
                v => SortKey.Create(v).Value)
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("pinned_sort_key")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.HasOne<Domain.Courses.Course>()
            .WithMany()
            .HasForeignKey(x => x.CourseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.AuthorId)
            .HasDatabaseName(CollectionsIndex.AUTHOR_ID);

        builder.HasIndex(x => x.CourseId)
            .HasFilter("course_id IS NOT NULL")
            .HasDatabaseName(CollectionsIndex.COURSE_ID);

        builder.HasIndex(x => new { x.AuthorId, x.Status })
            .HasDatabaseName(CollectionsIndex.AUTHOR_STATUS);
    }
}

public class CollectionSectionConfiguration : IEntityTypeConfiguration<CollectionSection>
{
    public void Configure(EntityTypeBuilder<CollectionSection> builder)
    {
        builder.ToTable("collection_sections");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.CollectionId)
            .IsRequired()
            .HasColumnName("collection_id");

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .HasColumnName("title")
            .IsRequired(false);

        builder.Property(x => x.Description)
            .HasColumnType("text")
            .HasColumnName("description")
            .IsRequired(false);

        builder.Property(x => x.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        // FK collection_id → collections.id (CASCADE). Навигации из Collection
        // намеренно нет — aggregate root детей не держит, см. Collection.cs docstring.
        builder.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(x => x.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.CollectionId, x.SortKey });
    }
}

public class CollectionItemConfiguration : IEntityTypeConfiguration<CollectionItem>
{
    public void Configure(EntityTypeBuilder<CollectionItem> builder)
    {
        builder.ToTable("collection_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.SectionId)
            .IsRequired()
            .HasColumnName("section_id");

        // Без HasDefaultValue: MATERIAL — первый член enum (default(TEnum)), в паре с
        // HasDefaultValue EF пропустил бы колонку в INSERT (см. Enum Storage Convention).
        // Значение всегда ставит конструктор CollectionItem. DB-default 'MATERIAL' задан
        // только в миграции GenericizeCollectionItems — backfill существующих строк.
        builder.Property(x => x.ItemType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("item_type")
            .IsRequired();

        // Generic-ссылка (materials.id | quizzes.id) — без FK, зеркало module_items.
        // Существование валидирует AddItemHandler, каскады — Delete-use-case'ы.
        builder.Property(x => x.ReferenceId)
            .IsRequired()
            .HasColumnName("reference_id");

        builder.Property(x => x.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        // FK section_id → collection_sections.id (CASCADE).
        builder.HasOne<CollectionSection>()
            .WithMany()
            .HasForeignKey(x => x.SectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SectionId, x.SortKey });

        builder.HasIndex(x => new { x.SectionId, x.ItemType, x.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ux_collection_items_section_reference");
    }
}
