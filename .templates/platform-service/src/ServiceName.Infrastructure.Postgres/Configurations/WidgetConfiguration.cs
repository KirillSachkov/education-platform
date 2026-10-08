using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceName.Domain.Widgets;

namespace ServiceName.Infrastructure.Postgres.Configurations;

public sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.ToTable("widgets");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");
        builder.Property(w => w.OwnerId).HasColumnName("owner_id");
        builder.Property(w => w.CreatedAt).HasColumnName("created_at");
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at");

        // Value object mapped to a plain string column.
        builder.Property(w => w.Name)
            .HasColumnName("name")
            .HasMaxLength(WidgetName.MAX_LENGTH)
            .IsRequired()
            .HasConversion(
                vo => vo.Value,
                raw => WidgetName.Of(raw).Value);

        builder.HasIndex(w => w.OwnerId).HasDatabaseName("ix_widgets_owner_id");
    }
}
