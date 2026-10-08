using TagService.Domain.EntityTags;
using TagService.Domain.TagAliases;
using TagService.Domain.Tags;
using Wolverine.EntityFrameworkCore;

namespace TagService.Infrastructure.Postgres;

public class TagDbContext : DbContext
{
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagAlias> TagAliases => Set<TagAlias>();
    public DbSet<EntityTag> EntityTags => Set<EntityTag>();

    public TagDbContext(DbContextOptions<TagDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tags");
        modelBuilder.MapWolverineEnvelopeStorage("tags");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TagDbContext).Assembly);
    }
}
