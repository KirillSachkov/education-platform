using System.Text.Json;
using FileService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileService.Infrastructure.Postgres.Configurations;

public sealed class VideoProviderRefConfiguration : IEntityTypeConfiguration<VideoProviderRef>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<VideoProviderRef> builder)
    {
        builder.ToTable("video_provider_refs");
        builder.HasKey(x => x.AssetId);

        builder.Property(x => x.AssetId)
            .HasColumnName("asset_id");

        builder.Property(x => x.ProviderCode)
            .HasConversion<string>()
            .HasColumnName("provider_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ExternalAssetId)
            .HasColumnName("external_asset_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v) ? null : JsonSerializer.Deserialize<VideoProviderMetadata>(v, JsonOptions));

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<MediaAsset>()
            .WithOne()
            .HasForeignKey<VideoProviderRef>(x => x.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ExternalAssetId);
    }
}
