using Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Bookmarks;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class MaterialBookmarkConfiguration : IEntityTypeConfiguration<MaterialBookmark>
{
    public const string USER_COURSE_TARGET_INDEX = "ux_material_bookmarks_user_id_course_id_target_entity_type_target_entity_id";
    public const string USER_CREATED_AT_INDEX = "ix_material_bookmarks_user_id_created_at_id";

    public void Configure(EntityTypeBuilder<MaterialBookmark> builder)
    {
        builder.ToTable("material_bookmarks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.Version)
            .IsRowVersion();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.CourseId)
            .IsRequired()
            .HasColumnName("course_id");

        builder.OwnsOne(x => x.EntityReference, entityReference =>
        {
            entityReference.Property(x => x.Type)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(EntityReference.MAX_TYPE_LENGTH)
                .HasColumnName("target_entity_type");

            entityReference.Property(x => x.Id)
                .IsRequired()
                .HasColumnName("target_entity_id");
        });

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id })
            .HasDatabaseName(USER_CREATED_AT_INDEX);

        // NOTE: The unique composite index backing the ON CONFLICT upsert in
        // MaterialBookmarkRepository — `(user_id, course_id, target_entity_type, target_entity_id)` —
        // is created via raw SQL in the RestoreMaterialBookmarksUniqueIndex migration.
        // EF Core 10's Fluent API HasIndex() cannot span owned-type navigation paths combined
        // with parent-scoped scalar properties, so the index is managed by migration only and
        // intentionally NOT declared here. A future `dotnet ef migrations add` will NOT
        // regenerate a drop for it because EF tracks indexes declared via Fluent API only.
    }
}
