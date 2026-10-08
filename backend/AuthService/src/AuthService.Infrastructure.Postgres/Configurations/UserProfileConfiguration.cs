using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;

namespace AuthService.Infrastructure.Postgres.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .IsRequired()
            .HasColumnType("uuid")
            .HasColumnName("id");

        builder.Property(x => x.Bio)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : Bio.Create(v).Value)
            .HasMaxLength(Bio.MAX_LENGTH)
            .IsRequired(false)
            .HasColumnName("bio");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.Property(x => x.AvatarId)
            .HasColumnType("uuid")
            .IsRequired(false)
            .HasColumnName("avatar_id");

        builder.Property(x => x.AvatarBindingRevision)
            .HasColumnName("avatar_binding_revision")
            .HasDefaultValue(-1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.OwnsOne(x => x.Profiles, b =>
        {
            b.ToJson("role_profiles");

            b.OwnsOne(p => p.Student, s =>
            {
                s.Property(x => x.GitHubUrl)
                    .HasConversion(
                        v => v == null ? null : v.Value,
                        v => v == null ? null : GitHubUrl.Create(v).Value)
                    .HasMaxLength(GitHubUrl.MAX_LENGTH);

                s.Property(x => x.UpdatedAt);
            });

            b.OwnsOne(p => p.Author, a =>
            {
                a.Property(x => x.Specialization)
                    .HasConversion(
                        v => v == null ? null : v.Value,
                        v => v == null ? null : Specialization.Create(v).Value)
                    .HasMaxLength(Specialization.MAX_LENGTH);

                a.Property(x => x.AboutAsAuthor)
                    .HasConversion(
                        v => v == null ? null : v.Value,
                        v => v == null ? null : AboutAsAuthor.Create(v).Value)
                    .HasMaxLength(AboutAsAuthor.MAX_LENGTH);

                a.Property(x => x.UpdatedAt);
            });

            b.OwnsOne(p => p.Reviewer, r =>
            {
                r.Property(x => x.ReviewCapacity);

                r.Property(x => x.Expertise)
                    .HasConversion(
                        v => v == null ? null : v.Value,
                        v => v == null ? null : Expertise.Create(v).Value)
                    .HasMaxLength(Expertise.MAX_LENGTH);

                r.Property(x => x.UpdatedAt);
            });
        });
    }
}
