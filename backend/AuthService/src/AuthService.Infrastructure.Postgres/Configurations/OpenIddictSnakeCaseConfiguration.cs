using OpenIddict.EntityFrameworkCore.Models;

namespace AuthService.Infrastructure.Postgres.Configurations;

internal static class OpenIddictSnakeCaseConfiguration
{
    public static void ConfigureOpenIddictSnakeCase(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>(b =>
        {
            b.ToTable("openiddict_applications");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ApplicationType).HasColumnName("application_type");
            b.Property(x => x.ClientId).HasColumnName("client_id");
            b.Property(x => x.ClientSecret).HasColumnName("client_secret");
            b.Property(x => x.ClientType).HasColumnName("client_type");
            b.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token");
            b.Property(x => x.ConsentType).HasColumnName("consent_type");
            b.Property(x => x.DisplayName).HasColumnName("display_name");
            b.Property(x => x.DisplayNames).HasColumnName("display_names");
            b.Property(x => x.JsonWebKeySet).HasColumnName("json_web_key_set");
            b.Property(x => x.Permissions).HasColumnName("permissions");
            b.Property(x => x.PostLogoutRedirectUris).HasColumnName("post_logout_redirect_uris");
            b.Property(x => x.Properties).HasColumnName("properties");
            b.Property(x => x.RedirectUris).HasColumnName("redirect_uris");
            b.Property(x => x.Requirements).HasColumnName("requirements");
            b.Property(x => x.Settings).HasColumnName("settings");
        });

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>(b =>
        {
            b.ToTable("openiddict_authorizations");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property<Guid?>("ApplicationId").HasColumnName("application_id");
            b.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token");
            b.Property(x => x.CreationDate).HasColumnName("creation_date");
            b.Property(x => x.Properties).HasColumnName("properties");
            b.Property(x => x.Scopes).HasColumnName("scopes");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.Subject).HasColumnName("subject");
            b.Property(x => x.Type).HasColumnName("type");
        });

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>(b =>
        {
            b.ToTable("openiddict_scopes");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token");
            b.Property(x => x.Description).HasColumnName("description");
            b.Property(x => x.Descriptions).HasColumnName("descriptions");
            b.Property(x => x.DisplayName).HasColumnName("display_name");
            b.Property(x => x.DisplayNames).HasColumnName("display_names");
            b.Property(x => x.Name).HasColumnName("name");
            b.Property(x => x.Properties).HasColumnName("properties");
            b.Property(x => x.Resources).HasColumnName("resources");
        });

        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>(b =>
        {
            b.ToTable("openiddict_tokens");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property<Guid?>("ApplicationId").HasColumnName("application_id");
            b.Property<Guid?>("AuthorizationId").HasColumnName("authorization_id");
            b.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token");
            b.Property(x => x.CreationDate).HasColumnName("creation_date");
            b.Property(x => x.ExpirationDate).HasColumnName("expiration_date");
            b.Property(x => x.Payload).HasColumnName("payload");
            b.Property(x => x.Properties).HasColumnName("properties");
            b.Property(x => x.RedemptionDate).HasColumnName("redemption_date");
            b.Property(x => x.ReferenceId).HasColumnName("reference_id");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.Subject).HasColumnName("subject");
            b.Property(x => x.Type).HasColumnName("type");

            // Single-column index on subject — FindBySubjectAsync / RevokeBySubjectAsync
            // (token revocation on role change) filter by subject alone. The default
            // OpenIddict composite index (application_id, status, subject, type) leads with
            // application_id, so it can't seek on a subject-only predicate → full table scan
            // on prod's large token table. (#588)
            b.HasIndex(x => x.Subject);
        });
    }
}
