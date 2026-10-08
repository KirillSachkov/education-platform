using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Users;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class ProgressUserConfiguration : IEntityTypeConfiguration<ProgressUser>
{
    public const string USERNAME_INDEX = "ix_progress_users_username";

    public void Configure(EntityTypeBuilder<ProgressUser> builder)
    {
        builder.ToTable("progress_users");

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserId)
            .ValueGeneratedNever()
            .HasColumnName("user_id");

        builder.Property(x => x.Username)
            .HasMaxLength(150)
            .HasColumnName("username");

        builder.Property(x => x.DisplayName)
            .HasMaxLength(150)
            .HasColumnName("display_name");

        builder.HasIndex(x => x.Username)
            .HasDatabaseName(USERNAME_INDEX);
    }
}
