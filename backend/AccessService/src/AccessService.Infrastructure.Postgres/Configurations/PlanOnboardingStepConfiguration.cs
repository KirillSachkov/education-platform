using AccessService.Domain.Onboarding;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;
using PlatformDatabase;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class PlanOnboardingStepConfiguration : IEntityTypeConfiguration<PlanOnboardingStep>
{
    public void Configure(EntityTypeBuilder<PlanOnboardingStep> b)
    {
        b.ToTable("plan_onboarding_steps");

        b.HasKey(s => s.Id);

        // EF generates Id via TimeOrderedGuidValueGenerator при Add (либо в DbSet,
        // либо через navigation collection). Domain ctor оставляет Id=Guid.Empty —
        // это сигнал EF что entity новая (Added state), а не detached existing.
        b.Property(s => s.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(s => s.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        b.Property(s => s.Type)
            .HasColumnName("step_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(s => s.IsSkippable)
            .HasColumnName("is_skippable")
            .IsRequired();

        b.Property(s => s.SortOrder)
            .HasColumnName("sort_order")
            .HasMaxLength(100)
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value);

        b.Property(s => s.Title)
            .HasColumnName("title")
            .HasMaxLength(PlanOnboardingStep.TITLE_MAX_LENGTH);

        b.Property(s => s.Body)
            .HasColumnName("body");

        b.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        b.HasIndex(s => new { s.PlanId, s.SortOrder })
            .HasDatabaseName("ix_plan_onboarding_steps_plan_order");
    }
}
