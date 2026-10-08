using CommentService.Domain;
using Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommentService.Infrastructure.Postgres.Configurations;

public static class CommentsIndex
{
    public const string PATH = "ix_comments_path";
    public const string TARGET_AUTHOR_ID = "ix_comments_target_author_id";
}

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => CommentId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.Property(x => x.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        builder.Property(x => x.TargetAuthorId)
            .HasColumnName("target_author_id")
            .IsRequired(false);

        builder.OwnsOne(x => x.EntityReference, entityReference =>
        {
            entityReference.Property(x => x.Type)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<EntityType>(v, true))
                .HasColumnName("target_entity_type")
                .HasMaxLength(EntityReference.MAX_TYPE_LENGTH)
                .IsRequired();

            entityReference.Property(x => x.Id)
                .HasColumnName("target_entity_id")
                .IsRequired();
        });

        builder.Property(x => x.Content)
            .HasConversion(
                v => v.Value,
                v => Content.Of(v).Value)
            .HasColumnName("content")
            .HasMaxLength(Content.MAX_LENGTH)
            .IsRequired();

        builder.OwnsOne(e => e.Path, pb =>
        {
            pb.Property(p => p.Value)
                .HasColumnType("ltree")
                .HasColumnName("path")
                .IsRequired();

            pb.Property(p => p.Depth)
                .HasColumnName("depth")
                .IsRequired();

            pb.HasIndex(e => e.Value)
                .HasMethod("gist")
                .HasDatabaseName(CommentsIndex.PATH);
        });

        builder.Property(x => x.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");

        builder.Property(x => x.DeletedAt)
            .IsRequired(false)
            .HasColumnName("deletion_date");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
