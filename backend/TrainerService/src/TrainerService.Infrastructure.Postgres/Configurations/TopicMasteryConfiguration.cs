using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.TopicMasteries;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TopicMasteryConfiguration : IEntityTypeConfiguration<TopicMastery>
{
    public void Configure(EntityTypeBuilder<TopicMastery> b)
    {
        b.ToTable("topic_masteries");

        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnName("id");

        b.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        b.Property(m => m.TopicId).HasColumnName("topic_id").IsRequired();

        b.Property(m => m.MasteryPercent).HasColumnName("mastery_percent").IsRequired();
        b.Property(m => m.AnswersCount).HasColumnName("answers_count").IsRequired();
        b.Property(m => m.LastPractisedAt).HasColumnName("last_practised_at").IsRequired();

        // IsWeak is a computed domain property — not persisted.
        b.Ignore(m => m.IsWeak);

        b.HasIndex(m => new { m.UserId, m.TopicId })
            .IsUnique()
            .HasDatabaseName("ux_topic_masteries_user_topic");
    }
}
