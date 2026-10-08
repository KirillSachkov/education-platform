using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class ModulesIndex
{
    public const string AUTHOR_ID = "ix_modules_author_id";
}

public class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.ToTable("modules");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(m => m.AuthorId)
            .IsRequired()
            .HasColumnName("author_id");

        builder.Property(x => x.Title)
            .HasConversion(
                v => v.Value,
                v => Title.Create(v).Value)
            .HasMaxLength(Title.MAX_LENGTH)
            .HasColumnName("title")
            .IsRequired();

        builder.Property(x => x.Description)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : Description.Create(v).Value)
            .HasMaxLength(Description.MAX_LENGTH)
            .HasColumnName("description")
            .IsRequired(false);

        builder.Property(x => x.DetailedDescription)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : DetailedDescription.Create(v).Value)
            .HasColumnName("detailed_description")
            .IsRequired(false);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(m => m.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.HasMany<ModuleItem>()
            .WithOne()
            .HasForeignKey(mi => mi.ModuleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Module titles are intentionally NOT unique — the same module name may be
        // reused across courses (and within a course). Modules are linked by Id, not title.
        builder.HasIndex(x => x.AuthorId).HasDatabaseName(ModulesIndex.AUTHOR_ID);
    }
}
