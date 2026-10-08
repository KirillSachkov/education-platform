using AuthService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Configurations.Identity;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("users");

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(30);
        builder.Property(x => x.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(30);
        builder.Property(x => x.Email).HasColumnName("email");
        builder.Property(x => x.NormalizedEmail).HasColumnName("normalized_email");
        builder.Property(x => x.EmailConfirmed).HasColumnName("email_confirmed");
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash");
        builder.Property(x => x.SecurityStamp).HasColumnName("security_stamp");
        builder.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number");
        builder.Property(x => x.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
        builder.Property(x => x.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        builder.Property(x => x.LockoutEnd).HasColumnName("lockout_end");
        builder.Property(x => x.LockoutEnabled).HasColumnName("lockout_enabled");
        builder.Property(x => x.AccessFailedCount).HasColumnName("access_failed_count");
    }
}
