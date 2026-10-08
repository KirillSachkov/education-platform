using System.Text.Json;
using FileService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileService.Infrastructure.Postgres.Configurations;

public sealed class FileStorageRefConfiguration : IEntityTypeConfiguration<FileStorageRef>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<FileStorageRef> builder)
    {
        builder.ToTable("file_storage_refs");
        builder.HasKey(x => x.AssetId);

        builder.Property(x => x.AssetId)
            .HasColumnName("asset_id");

        builder.Property(x => x.StorageKey)
            .HasConversion(v => v.Value, v => StorageKey.Of(v).Value)
            .HasColumnName("storage_key")
            .HasMaxLength(StorageKey.MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? null : JsonSerializer.Deserialize<FileStorageMetadata>(v, JsonOptions));

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<MediaAsset>()
            .WithOne()
            .HasForeignKey<FileStorageRef>(x => x.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.StorageKey).IsUnique();
    }
}
