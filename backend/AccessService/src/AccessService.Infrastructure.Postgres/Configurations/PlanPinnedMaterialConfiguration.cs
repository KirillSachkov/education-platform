using AccessService.Domain;
using AccessService.Domain.HomePins;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class PlanPinnedMaterialConfiguration : IEntityTypeConfiguration<PlanPinnedMaterial>
{
    public void Configure(EntityTypeBuilder<PlanPinnedMaterial> b)
    {
        b.ToTable("plan_pinned_materials");

        b.HasKey(x => x.Id);

        // Aggregate root saved via DbSet.AddAsync — PK генерируется в factory
        // (Guid.CreateVersion7), без ValueGenerator. См. backend-transactions.md правило 4.
        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        b.Property(x => x.MaterialId)
            .HasColumnName("material_id")
            .IsRequired();

        b.Property(x => x.Note)
            .HasColumnName("note")
            .HasMaxLength(PlanPinnedMaterial.NOTE_MAX_LENGTH);

        b.Property(x => x.SortKey)
            .HasColumnName("sort_key")
            .HasMaxLength(100)
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value);

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Один материал — один закреп на план.
        b.HasIndex(x => new { x.PlanId, x.MaterialId })
            .IsUnique()
            .HasDatabaseName("uq_plan_pinned_materials_plan_material");

        // Упорядоченное чтение закрепов плана.
        b.HasIndex(x => new { x.PlanId, x.SortKey })
            .HasDatabaseName("ix_plan_pinned_materials_plan_sort");

        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
