using System.Text.Json;
using FileService.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileService.Infrastructure.Postgres.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasColumnName("kind")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.UsageType)
            .HasConversion<string>()
            .HasColumnName("usage_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasConversion(v => v.Value, v => FileName.Of(v).Value)
            .HasColumnName("file_name")
            .HasMaxLength(FileName.MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .HasConversion(v => v.Value, v => MediaContentType.Of(v).Value)
            .HasColumnName("content_type")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.Size).HasColumnName("size");
        builder.Property(x => x.DraftId).HasColumnName("draft_id");
        builder.Property(x => x.IsTemporary).HasColumnName("is_temporary");
        builder.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.BoundAt).HasColumnName("bound_at");
        builder.Property(x => x.BindingRevision)
            .HasColumnName("binding_revision")
            .HasDefaultValue(0L)
            .IsRequired();
        builder.Property(x => x.BindingSelectionId)
            .HasColumnName("binding_selection_id");
        builder.Property(x => x.ConfirmedBindingRevision)
            .HasColumnName("confirmed_binding_revision")
            .HasDefaultValue(-1L)
            .IsRequired();
        builder.Property(x => x.DetachedThroughBindingRevision)
            .HasColumnName("detached_through_binding_revision")
            .HasDefaultValue(-1L)
            .IsRequired();
        builder.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
        builder.Property(x => x.ProcessingStartedAt).HasColumnName("processing_started_at");
        builder.Property(x => x.DeleteRequestedAt).HasColumnName("delete_requested_at");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        // Responsive image variants (#646) — JSONB blob on the backing field. Mirrors the
        // explicit-converter approach used for file_storage_refs.metadata: a whole-object
        // (de)serialize keeps the variant list a self-contained value with no extra schema.
        // NB: JsonSerializerDefaults.Web ⇒ keys are camelCase on disk
        // (`width`, `storageKey`, `contentType`, `size`). Any future Dapper/raw-SQL query
        // that reads `media_assets.image_variants` must match that casing.
        builder.Property<List<ImageVariant>>("_imageVariants")
            .HasColumnName("image_variants")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<ImageVariant>()
                    : JsonSerializer.Deserialize<List<ImageVariant>>(v, JsonOptions) ?? new List<ImageVariant>())
            .Metadata.SetValueComparer(new ValueComparer<List<ImageVariant>>(
                (a, b) => (a ?? new List<ImageVariant>()).SequenceEqual(b ?? new List<ImageVariant>()),
                v => v == null ? 0 : v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                v => v == null ? new List<ImageVariant>() : v.ToList()));

        builder.OwnsOne(x => x.TargetEntity, target =>
        {
            target.Property(x => x.Type)
                .HasColumnName("target_entity_type")
                .HasMaxLength(TargetEntity.MAX_TYPE_LENGTH);

            target.Property(x => x.Id)
                .HasColumnName("target_entity_id");

            target.HasIndex(x => new { x.Id, x.Type });
        });

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.UsageType);
        builder.HasIndex(x => x.DraftId);
    }
}
