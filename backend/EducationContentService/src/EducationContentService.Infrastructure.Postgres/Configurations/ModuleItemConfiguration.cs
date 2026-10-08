using EducationContentService.Domain.Modules;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public class ModuleItemConfiguration : IEntityTypeConfiguration<ModuleItem>
{
    public void Configure(EntityTypeBuilder<ModuleItem> builder)
    {
        builder.ToTable("module_items");

        builder.HasKey(mi => mi.Id);

        builder.Property(mi => mi.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(mi => mi.ModuleId)
            .IsRequired()
            .HasColumnName("module_id");

        builder.Property(mi => mi.ItemType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("item_type")
            .IsRequired();

        builder.Property(mi => mi.ReferenceId)
            .IsRequired()
            .HasColumnName("reference_id");

        builder.Property(mi => mi.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        builder.Property(mi => mi.IsOptional)
            .IsRequired()
            .HasColumnName("is_optional");

        // Без HasDefaultValue: ViewPriority.Key — первый член enum (default(TEnum)),
        // в паре с HasDefaultValue EF трактует Key как «значение не задано», пропускает
        // колонку в INSERT, и БД подставляет DEFAULT 'Recommended'. Значение задаётся
        // в ctor/handler-ах (Material → Key, Issue → Recommended).
        builder.Property(mi => mi.ViewPriority)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("view_priority")
            .IsRequired();

        builder.HasIndex(mi => new { mi.ModuleId, mi.SortKey });
        builder.HasIndex(mi => new { mi.ReferenceId, mi.ItemType, mi.ModuleId });

        // Инвариант: задача (Issue) может принадлежать только одному модулю.
        // Частичный уникальный индекс — применяется только к строкам с item_type = 'Issue'.
        builder
            .HasIndex(mi => mi.ReferenceId)
            .IsUnique()
            .HasFilter("item_type = 'Issue'")
            .HasDatabaseName("ux_module_items_issue_reference");
    }
}