using CommentService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommentService.Infrastructure.Postgres.Configurations;

public sealed class AuthorFeedStateConfiguration : IEntityTypeConfiguration<AuthorFeedState>
{
    public void Configure(EntityTypeBuilder<AuthorFeedState> builder)
    {
        builder.ToTable("author_feed_state");

        builder.HasKey(x => x.AuthorId);

        builder.Property(x => x.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        builder.Property(x => x.ViewedAt)
            .HasColumnName("viewed_at")
            .IsRequired();
    }
}
