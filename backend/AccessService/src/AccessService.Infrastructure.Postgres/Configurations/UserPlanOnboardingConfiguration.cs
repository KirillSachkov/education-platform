using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class UserPlanOnboardingConfiguration : IEntityTypeConfiguration<UserPlanOnboarding>
{
    public void Configure(EntityTypeBuilder<UserPlanOnboarding> b)
    {
        b.ToTable("user_plan_onboardings");

        b.HasKey(u => new { u.UserId, u.PlanId });

        b.Property(u => u.UserId).HasColumnName("user_id");

        b.Property(u => u.PlanId).HasColumnName("plan_id");

        b.Property(u => u.StartedAt).HasColumnName("started_at").IsRequired();

        b.Property(u => u.CompletedAt).HasColumnName("completed_at");

        b.Property(u => u.CurrentStepId).HasColumnName("current_step_id");

        ValueComparer<IReadOnlyList<Guid>> guidListComparer = new(
            (a, c) => (a == null && c == null) || (a != null && c != null && a.SequenceEqual(c)),
            v => v.Aggregate(0, (acc, g) => HashCode.Combine(acc, g.GetHashCode())),
            v => (IReadOnlyList<Guid>)v.ToList());

        b.Property(u => u.SkippedStepIds)
            .HasColumnName("skipped_step_ids")
            .HasColumnType("uuid[]")
            .HasConversion(
                v => v.ToArray(),
                v => (IReadOnlyList<Guid>)v.ToList())
            .Metadata.SetValueComparer(guidListComparer);

        b.Property(u => u.CompletedStepIds)
            .HasColumnName("completed_step_ids")
            .HasColumnType("uuid[]")
            .HasConversion(
                v => v.ToArray(),
                v => (IReadOnlyList<Guid>)v.ToList())
            .Metadata.SetValueComparer(guidListComparer);

        b.HasIndex(u => new { u.UserId, u.StartedAt })
            .HasDatabaseName("ix_user_plan_onboardings_user_pending")
            .HasFilter("completed_at IS NULL");

        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(u => u.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
