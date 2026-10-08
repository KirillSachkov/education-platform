using AuthService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Configurations;

internal sealed class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public void Configure(EntityTypeBuilder<UserConsent> builder)
    {
        builder.ToTable("user_consents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .IsRequired()
            .HasColumnType("uuid")
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnType("uuid")
            .HasColumnName("user_id");

        builder.Property(x => x.ConsentType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("consent_type");

        builder.Property(x => x.DocumentVersion)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("document_version");

        builder.Property(x => x.AcceptedAt)
            .IsRequired()
            .HasColumnName("accepted_at");

        builder.Property(x => x.IpAddress)
            .IsRequired()
            .HasMaxLength(45) // IPv6 max
            .HasColumnName("ip_address");

        builder.Property(x => x.UserAgent)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("user_agent");

        builder.HasIndex(x => new { x.UserId, x.ConsentType })
            .HasDatabaseName("ix_user_consents_user_type");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
