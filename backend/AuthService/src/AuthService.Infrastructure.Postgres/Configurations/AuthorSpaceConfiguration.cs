using AuthService.Domain.AuthorSpaces;
using AuthService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Configurations;

public class AuthorSpaceConfiguration : IEntityTypeConfiguration<AuthorSpace>
{
    public void Configure(EntityTypeBuilder<AuthorSpace> builder)
    {
        builder.ToTable("author_spaces");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .IsRequired()
            .HasColumnType("uuid")
            .HasColumnName("id");

        builder.Property(x => x.Slug)
            .HasConversion(
                v => v.Value,
                v => AuthorSpaceSlug.Create(v).Value)
            .HasMaxLength(AuthorSpaceSlug.MAX_LENGTH)
            .IsRequired()
            .HasColumnName("slug");

        builder.HasIndex(x => x.Slug)
            .IsUnique();

        builder.Property(x => x.Tagline)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : Tagline.Create(v).Value)
            .HasMaxLength(Tagline.MAX_LENGTH)
            .IsRequired(false)
            .HasColumnName("tagline");

        builder.Property(x => x.LogoAssetId)
            .HasColumnType("uuid")
            .IsRequired(false)
            .HasColumnName("logo_asset_id");

        builder.OwnsOne(x => x.FeatureFlags, b =>
        {
            b.ToJson("feature_flags");

            b.Property(f => f.GitHubIntegration);
            b.Property(f => f.PrReviews);
            b.Property(f => f.AiAssistant);
            b.Property(f => f.Roadmaps);
            b.Property(f => f.CustomLanding);
        });

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");
    }
}