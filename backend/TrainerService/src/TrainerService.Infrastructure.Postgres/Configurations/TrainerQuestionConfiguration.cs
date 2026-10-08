using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TrainerQuestionConfiguration : IEntityTypeConfiguration<TrainerQuestion>
{
    public void Configure(EntityTypeBuilder<TrainerQuestion> b)
    {
        b.ToTable("trainer_questions");

        b.HasKey(q => q.Id);
        b.Property(q => q.Id).HasColumnName("id");

        b.Property(q => q.BankId).HasColumnName("bank_id").IsRequired();

        b.Property(q => q.Stem).HasColumnName("stem").IsRequired();

        b.Property(q => q.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(q => q.ReferenceAnswer).HasColumnName("reference_answer");

        b.Property(q => q.Explanation).HasColumnName("explanation");

        b.Property(q => q.Difficulty)
            .HasColumnName("difficulty")
            .HasConversion<string>()
            .HasMaxLength(50);

        b.Property(q => q.Section).HasColumnName("section").HasMaxLength(100);

        b.Property(q => q.SortKey).HasColumnName("sort_key").HasMaxLength(100).IsRequired();

        b.Property(q => q.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(q => q.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Free-sample flag (#674): материализованный per-question gate. NOT NULL DEFAULT false → существующие
        // строки заперты до backfill `recompute-free-samples`. Ставится TrainerFreeAllocationPolicy.
        b.Property(q => q.IsFreeSample)
            .HasColumnName("is_free_sample")
            .HasDefaultValue(false)
            .IsRequired();

        // Computed projection from Options' IsCorrect — not a column.
        b.Ignore(q => q.CorrectOptionIds);

        // FK на банк-владелец: удаление банка каскадно сносит его вопросы (а те — свои опции).
        // Индекс на bank_id создаётся EF автоматически как часть FK (отдельный HasIndex не нужен).
        b.HasOne<TopicBank>()
            .WithMany()
            .HasForeignKey(q => q.BankId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(q => new { q.BankId, q.SortKey })
            .HasDatabaseName("ix_trainer_questions_bank_sort_key");

        // Опции — own child table. Field-access: Options доступна через getter `=> _options`,
        // иначе EF трекает новый элемент Modified вместо Added (см. MockInterviewConfiguration).
        b.HasMany(q => q.Options)
            .WithOne()
            .HasForeignKey("trainer_question_id")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(q => q.Options)
            .AutoInclude()
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_options");
    }
}

internal sealed class TrainerQuestionOptionConfiguration : IEntityTypeConfiguration<TrainerQuestionOption>
{
    public void Configure(EntityTypeBuilder<TrainerQuestionOption> b)
    {
        b.ToTable("trainer_question_options");

        b.HasKey(o => o.Id);

        // EF generates Id via TimeOrderedGuidValueGenerator on Add (nav-collection child).
        // Domain factory leaves Id=Guid.Empty. See docs/agents/backend-transactions.md rule 4.
        b.Property(o => o.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(o => o.Text).HasColumnName("text").IsRequired();

        b.Property(o => o.IsCorrect).HasColumnName("is_correct").IsRequired();

        b.Property(o => o.SortIndex).HasColumnName("sort_index").IsRequired();

        b.HasIndex("trainer_question_id", "SortIndex")
            .HasDatabaseName("ix_trainer_question_options_question_sort");
    }
}
