using AccessService.Domain.Billing;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class BillingConfigConfiguration : IEntityTypeConfiguration<BillingConfig>
{
    public void Configure(EntityTypeBuilder<BillingConfig> builder)
    {
        builder.ToTable("billing_config");

        builder.HasKey(x => x.Id);
        // PK — фиксированный singleton-Guid из factory, не БД-generated.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
