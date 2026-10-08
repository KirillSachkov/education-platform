using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ShortLinks;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class ShortLinksIndex
{
    public const string CODE = "ux_short_links_code";
    public const string MATERIAL_ID = "ux_short_links_material_id";
}

public class ShortLinkConfiguration : IEntityTypeConfiguration<ShortLink>
{
    public void Configure(EntityTypeBuilder<ShortLink> builder)
    {
        builder.ToTable("short_links");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .IsRequired()
            .HasColumnName("id");

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(16)
            .HasColumnName("code");

        builder.Property(x => x.MaterialId)
            .IsRequired()
            .HasColumnName("material_id");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName(ShortLinksIndex.CODE);

        // Ровно один код на материал — get-or-create идемпотентен, гонки отбивает индекс.
        builder.HasIndex(x => x.MaterialId)
            .IsUnique()
            .HasDatabaseName(ShortLinksIndex.MATERIAL_ID);

        // ON DELETE CASCADE (в отличие от Restrict у quizzes/collection_items): short_link —
        // производная ссылка без собственного контента, БД чистит её при hard-delete материала
        // сама — расширять cascade-SQL в DeleteMaterialHandler не требуется.
        builder.HasOne<Material>()
            .WithMany()
            .HasForeignKey(x => x.MaterialId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
