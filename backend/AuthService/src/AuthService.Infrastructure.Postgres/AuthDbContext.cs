using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using AuthService.Domain;
using AuthService.Domain.AdminAuditLog;
using AuthService.Domain.AuthorSpaces;
using AuthService.Infrastructure.Postgres.Configurations;
using Wolverine.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres;

public sealed class AuthDbContext : IdentityDbContext<Account, Role, Guid>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<AuthorSpace> AuthorSpaces => Set<AuthorSpace>();

    public DbSet<UserGithubOrg> UserGithubOrgs => Set<UserGithubOrg>();

    public DbSet<UserConsent> UserConsents => Set<UserConsent>();

    public DbSet<AdminAuditLogEntry> AdminAuditLogs => Set<AdminAuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("auth");
        modelBuilder.MapWolverineEnvelopeStorage("auth");

        // UserProfile → FK to Account (one-to-one)
        modelBuilder.Entity<UserProfile>(b =>
        {
            b.HasOne<Account>()
                .WithOne()
                .HasForeignKey<UserProfile>(p => p.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuthorSpace → FK to Account (one-to-one)
        modelBuilder.Entity<AuthorSpace>(b =>
        {
            b.HasOne<Account>()
                .WithOne()
                .HasForeignKey<AuthorSpace>(s => s.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.UseOpenIddict<Guid>();
        modelBuilder.ConfigureOpenIddictSnakeCase();

        // Identity (Account/Role + IdentityUserClaim/Login/Token/Role + IdentityRoleClaim)
        // and other domain configurations live in Configurations/.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
    }
}
