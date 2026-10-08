using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Configurations.Identity;

public sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.ToTable("user_tokens");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.LoginProvider).HasColumnName("login_provider");
        builder.Property(x => x.Name).HasColumnName("name");
        builder.Property(x => x.Value).HasColumnName("value");
    }
}
