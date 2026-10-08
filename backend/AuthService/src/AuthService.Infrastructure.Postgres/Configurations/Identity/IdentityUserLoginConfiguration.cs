using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Configurations.Identity;

public sealed class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.ToTable("user_logins");

        builder.Property(x => x.LoginProvider).HasColumnName("login_provider");
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key");
        builder.Property(x => x.ProviderDisplayName).HasColumnName("provider_display_name");
        builder.Property(x => x.UserId).HasColumnName("user_id");
    }
}
