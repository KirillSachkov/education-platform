using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Notes;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class MaterialNoteConfiguration : IEntityTypeConfiguration<MaterialNote>
{
    public const string USER_MATERIAL_INDEX = "ux_material_notes_user_id_material_id";

    public void Configure(EntityTypeBuilder<MaterialNote> builder)
    {
        builder.ToTable("material_notes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.MaterialId)
            .IsRequired()
            .HasColumnName("material_id");

        builder.Property(x => x.Content)
            .IsRequired()
            .HasMaxLength(MaterialNote.MAX_CONTENT_LENGTH)
            .HasColumnName("content");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        // Unique по паре (user, material) — главный инвариант: одна заметка на пользователя-материал.
        builder.HasIndex(x => new { x.UserId, x.MaterialId })
            .HasDatabaseName(USER_MATERIAL_INDEX)
            .IsUnique();
    }
}
