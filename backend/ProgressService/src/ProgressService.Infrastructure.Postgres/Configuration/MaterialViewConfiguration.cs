using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Materials;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class MaterialViewConfiguration : IEntityTypeConfiguration<MaterialView>
{
    public const string USER_MATERIAL_INDEX = "ux_material_views_user_id_material_id";
    public const string MATERIAL_INDEX = "ix_material_views_material_id";

    public void Configure(EntityTypeBuilder<MaterialView> builder)
    {
        builder.ToTable("material_views");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.MaterialId)
            .IsRequired()
            .HasColumnName("material_id");

        builder.Property(x => x.ViewedAt)
            .IsRequired()
            .HasColumnName("viewed_at");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        // is_completed — флаг «явно отмечено изученным». Default false (silent track-view).
        // True — пользователь нажал «Отметить изученным», что каскадит module_item_progress
        // и начисляет XP (см. <c>MaterialViewedEvent</c>). Issue #285.
        builder.Property(x => x.IsCompleted)
            .IsRequired()
            .HasColumnName("is_completed");

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        // Unique по паре (user, material) — главный инвариант: один просмотр на пользователя-материал.
        builder.HasIndex(x => new { x.UserId, x.MaterialId })
            .HasDatabaseName(USER_MATERIAL_INDEX)
            .IsUnique();

        // Для batch-cleanup при MaterialHardDeleted (DELETE WHERE material_id = ANY).
        builder.HasIndex(x => x.MaterialId)
            .HasDatabaseName(MATERIAL_INDEX);
    }
}
