using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SearchService.Core.Reindex.State;

namespace SearchService.Infrastructure.Postgres.Configurations;

public sealed class SearchReindexStateConfiguration : IEntityTypeConfiguration<SearchReindexState>
{
    public void Configure(EntityTypeBuilder<SearchReindexState> builder)
    {
        builder.ToTable("reindex_state", t =>
        {
            // Singleton-таблица: ровно одна строка с id = 1. Constraint мешает кому-то
            // случайно INSERT'нуть вторую запись raw-SQL'ом в обход репозитория.
            t.HasCheckConstraint("ck_reindex_state_singleton", "id = 1");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.AppliedGeneration)
            .HasColumnName("applied_generation")
            .IsRequired();

        builder.Property(s => s.AppliedSchemaHash)
            .HasColumnName("applied_schema_hash")
            .HasMaxLength(128);

        builder.Property(s => s.AppliedDeployStamp)
            .HasColumnName("applied_deploy_stamp")
            .HasMaxLength(256);

        builder.Property(s => s.LastAppliedAtUtc)
            .HasColumnName("last_applied_at_utc");

        builder.Property(s => s.LastRequestId)
            .HasColumnName("last_request_id");
    }
}
