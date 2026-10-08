using AuthService.Domain.AdminAuditLog;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Configurations;

public sealed class AdminAuditLogEntryConfiguration : IEntityTypeConfiguration<AdminAuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AdminAuditLogEntry> builder)
    {
        builder.ToTable("admin_audit_log");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AdminId).HasColumnName("admin_id");
        builder.Property(e => e.TargetUserId).HasColumnName("target_user_id");
        builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Method).HasColumnName("method").HasMaxLength(10).IsRequired();
        builder.Property(e => e.Path).HasColumnName("path").HasMaxLength(500).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(e => e.Result).HasColumnName("result").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(500);

        builder.HasIndex(e => new { e.AdminId, e.CreatedAt })
            .HasDatabaseName("ix_admin_audit_log_admin");

        builder.HasIndex(e => new { e.Action, e.CreatedAt })
            .HasDatabaseName("ix_admin_audit_log_action");

        // Partial index on target_user_id is added via raw SQL in the migration
        // (Fluent API doesn't support WHERE clauses on indexes cleanly).
    }
}
