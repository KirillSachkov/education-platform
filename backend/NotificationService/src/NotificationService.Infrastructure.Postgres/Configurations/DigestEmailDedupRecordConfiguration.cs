using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Infrastructure.Postgres.Digest;

namespace NotificationService.Infrastructure.Postgres.Configurations;

public sealed class DigestEmailDedupRecordConfiguration : IEntityTypeConfiguration<DigestEmailDedupRecord>
{
    public void Configure(EntityTypeBuilder<DigestEmailDedupRecord> builder)
    {
        builder.ToTable("digest_email_dedup");

        builder.HasKey(x => new { x.CorrelationId, x.InboxHash });

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(x => x.InboxHash)
            .HasColumnName("inbox_hash")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();
    }
}
