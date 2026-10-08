using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Materials;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class AnonymousMaterialViewConfiguration : IEntityTypeConfiguration<AnonymousMaterialView>
{
    public const string ANON_MATERIAL_INDEX = "ux_anonymous_material_views_anon_id_material_id";
    public const string MATERIAL_INDEX = "ix_anonymous_material_views_material_id";

    public void Configure(EntityTypeBuilder<AnonymousMaterialView> builder)
    {
        builder.ToTable("anonymous_material_views");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.AnonymousId)
            .IsRequired()
            .HasMaxLength(AnonymousMaterialView.ANONYMOUS_ID_MAX_LENGTH)
            .HasColumnName("anonymous_id");

        builder.Property(x => x.MaterialId)
            .IsRequired()
            .HasColumnName("material_id");

        builder.Property(x => x.ViewedAt)
            .IsRequired()
            .HasColumnName("viewed_at");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        // Unique по паре (anonymous_id, material_id) — главный инвариант: один просмотр на cookie+material.
        builder.HasIndex(x => new { x.AnonymousId, x.MaterialId })
            .HasDatabaseName(ANON_MATERIAL_INDEX)
            .IsUnique();

        // Для batch-counts'а в GET /progress/materials/views/counts (GROUP BY material_id).
        builder.HasIndex(x => x.MaterialId)
            .HasDatabaseName(MATERIAL_INDEX);
    }
}
