using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TagService.Domain.Tags;

namespace TagService.Infrastructure.Postgres.Configurations;

public static class TagsIndex
{
    public const string SLUG_UNIQUE = "ux_tags_slug";
    public const string AUTHOR_SLUG_UNIQUE = "ux_tags_author_slug";
    public const string TITLE_TRGM = "ix_tags_title_trgm";
    public const string AUTHOR_ID = "ix_tags_author_id";
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags", table =>
            table.HasCheckConstraint("ck_tags_author_id_not_empty", "author_id <> '00000000-0000-0000-0000-000000000000'"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => TagId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.Property(x => x.Title)
            .HasConversion(
                v => v.Value,
                v => TagTitle.Of(v).Value)
            .HasColumnName("title")
            .HasMaxLength(TagTitle.MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasConversion(
                v => v.Value,
                v => TagSlug.Of(v).Value)
            .HasColumnName("slug")
            .HasMaxLength(TagSlug.MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasColumnName("kind")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.HasIndex(x => new { x.AuthorId, x.Slug })
            .IsUnique()
            .HasDatabaseName(TagsIndex.AUTHOR_SLUG_UNIQUE);

        builder.HasIndex(x => x.Title)
            .HasDatabaseName(TagsIndex.TITLE_TRGM)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.HasIndex(x => x.AuthorId)
            .HasDatabaseName(TagsIndex.AUTHOR_ID);

        builder.Navigation(x => x.Aliases)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

    }
}
