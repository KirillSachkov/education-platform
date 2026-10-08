using AssignmentReviewService.Domain.AiSettings;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class AiModelSettingsConfiguration : IEntityTypeConfiguration<AiModelSettings>
{
    public void Configure(EntityTypeBuilder<AiModelSettings> builder)
    {
        builder.ToTable("ai_model_settings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        ConfigureSlot(builder, x => x.Reviewer!, "reviewer");

        builder.Property(x => x.ReviewerBasePrompt)
            .HasColumnName("reviewer_base_prompt")
            .HasMaxLength(AiModelSettings.MAX_REVIEWER_BASE_PROMPT_LENGTH);

        builder.Property(x => x.ReviewEnabled)
            .HasColumnName("review_enabled");

        builder.Property(x => x.RepoContextEnabled)
            .HasColumnName("repo_context_enabled");

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.UpdatedByUserId)
            .HasColumnName("updated_by_user_id");
    }

    private static void ConfigureSlot(
        EntityTypeBuilder<AiModelSettings> builder,
        System.Linq.Expressions.Expression<Func<AiModelSettings, AiModelSlot?>> selector,
        string columnPrefix)
    {
        builder.OwnsOne(selector, slot =>
        {
            slot.Property(s => s.Model)
                .HasColumnName($"{columnPrefix}_model")
                .HasMaxLength(AiModelSlot.MAX_MODEL_LENGTH)
                .IsRequired();

            slot.Property(s => s.Temperature)
                .HasColumnName($"{columnPrefix}_temperature");

            slot.Property(s => s.MaxOutputTokens)
                .HasColumnName($"{columnPrefix}_max_output_tokens");

            slot.Property(s => s.TimeoutSeconds)
                .HasColumnName($"{columnPrefix}_timeout_seconds");
        });

        builder.Navigation(selector).IsRequired();
    }
}
