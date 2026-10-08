using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TagService.Domain.TagAliases;
using TagService.Domain.Tags;

namespace TagService.Infrastructure.Postgres.Configurations;

public static class TagAliasesIndex
{
    public const string TAG_ALIAS_UNIQUE = "ux_tag_aliases_tag_id_alias_tag_id";
    public const string ALIAS_TAG_UNIQUE = "ux_tag_aliases_alias_tag_id";
}

public sealed class TagAliasConfiguration : IEntityTypeConfiguration<TagAlias>
{
    public void Configure(EntityTypeBuilder<TagAlias> builder)
    {
        builder.ToTable("tag_aliases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => TagAliasId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.Property(x => x.TagId)
            .HasConversion(
                v => v.Value,
                v => TagId.Of(v))
            .HasColumnName("tag_id")
            .IsRequired();

        builder.Property(x => x.AliasTagId)
            .HasConversion(
                v => v.Value,
                v => TagId.Of(v))
            .HasColumnName("alias_tag_id")
            .IsRequired();

        builder.HasIndex(x => new { x.TagId, x.AliasTagId })
            .IsUnique()
            .HasDatabaseName(TagAliasesIndex.TAG_ALIAS_UNIQUE);

        builder.HasIndex(x => x.AliasTagId)
            .IsUnique()
            .HasDatabaseName(TagAliasesIndex.ALIAS_TAG_UNIQUE);

        builder.HasOne<Tag>()
            .WithMany(x => x.Aliases)
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(x => x.AliasTagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
