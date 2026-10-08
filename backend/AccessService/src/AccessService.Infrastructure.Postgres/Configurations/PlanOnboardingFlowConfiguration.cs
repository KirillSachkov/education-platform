using AccessService.Domain.Onboarding;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class PlanOnboardingFlowConfiguration : IEntityTypeConfiguration<PlanOnboardingFlow>
{
    public void Configure(EntityTypeBuilder<PlanOnboardingFlow> b)
    {
        b.ToTable("plan_onboarding_flows");

        b.HasKey(f => f.PlanId);

        b.Property(f => f.PlanId).HasColumnName("plan_id");

        b.Property(f => f.IsEnabled)
            .HasColumnName("is_enabled")
            .IsRequired();

        b.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Owned collection — шаги хранятся в отдельной таблице.
        // FK на Step.PlanId — это shared property, EF использует существующее свойство домена,
        // а не shadow column. PrincipalKey = Flow.PlanId (PK).
        b.HasMany(f => f.Steps)
            .WithOne()
            .HasForeignKey(s => s.PlanId)
            .HasPrincipalKey(f => f.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // Field-access — мы возвращаем `_steps` через `Steps => _steps` getter.
        // Без `Field`-mode EF читает navigation как property (read-only IReadOnlyList)
        // и при Add к private `_steps` change tracker не видит новый entity.
        // Symptom: новый PlanOnboardingStep попадает в trackerе как Modified, не Added,
        // → UPDATE WHERE id=<новый guid> на 0 rows → DbUpdateConcurrencyException.
        b.Navigation(f => f.Steps)
            .AutoInclude()
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_steps");

        // FK на access.plans(id) — flow.plan_id ссылается на plans.id.
        b.HasOne<Domain.Plan>()
            .WithOne()
            .HasForeignKey<PlanOnboardingFlow>(f => f.PlanId)
            .HasPrincipalKey<Domain.Plan>(p => p.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
