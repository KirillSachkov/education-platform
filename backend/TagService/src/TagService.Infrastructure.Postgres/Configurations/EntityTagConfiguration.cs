using Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TagService.Domain.EntityTags;
using TagService.Domain.Tags;

namespace TagService.Infrastructure.Postgres.Configurations;

public static class EntityTagsIndex
{
    public const string ENTITY_TAG_UNIQUE = "ux_entity_tags_entity_type_entity_id_tag_id";
}

public sealed class EntityTagConfiguration : IEntityTypeConfiguration<EntityTag>
{
    public void Configure(EntityTypeBuilder<EntityTag> builder)
    {
        builder.ToTable("entity_tags");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => EntityTagId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.OwnsOne(x => x.EntityReference, entityReference =>
        {
            entityReference.Property(x => x.Type)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<EntityType>(v, true))
                .HasColumnName("entity_type")
                .HasMaxLength(EntityReference.MAX_TYPE_LENGTH)
                .IsRequired();

            entityReference.Property(x => x.Id)
                .HasColumnName("entity_id")
                .IsRequired();
        });

        builder.Property(x => x.TagId)
            .HasConversion(
                v => v.Value,
                v => TagId.Of(v))
            .HasColumnName("tag_id")
            .IsRequired();

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TagId)
            .HasDatabaseName("ix_entity_tags_tag_id");

        // NOTE: the composite unique index `ux_entity_tags_entity_type_entity_id_tag_id`
        // was created directly via raw SQL in the initial migration (20260310100544_Initial)
        // because EF Core cannot express a composite index that spans owned-type properties
        // (EntityReference.Type / EntityReference.Id) and the parent entity's TagId in Fluent
        // API. As a consequence the index is present in the DB schema but not in the EF model
        // snapshot. When adding future migrations, verify that `dotnet ef migrations add`
        // does not emit a spurious CreateIndex for this index.
    }
}
