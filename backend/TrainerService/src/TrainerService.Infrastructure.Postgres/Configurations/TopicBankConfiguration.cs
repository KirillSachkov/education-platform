using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TopicBankConfiguration : IEntityTypeConfiguration<TopicBank>
{
    public void Configure(EntityTypeBuilder<TopicBank> b)
    {
        b.ToTable("topic_banks");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.TopicId).HasColumnName("topic_id").IsRequired();

        b.Property(x => x.Tier)
            .HasColumnName("tier")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.Difficulty)
            .HasColumnName("difficulty")
            .HasConversion<string>()
            .HasMaxLength(50);

        b.Property(x => x.SortKey)
            .HasColumnName("sort_key")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.Purpose)
            .HasColumnName("purpose")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => new { x.TopicId, x.SortKey })
            .HasDatabaseName("ix_topic_banks_topic_sort_key");
    }
}
