using SearchService.Core.Reindex.State;
using Wolverine.EntityFrameworkCore;

namespace SearchService.Infrastructure.Postgres;

public class SearchDbContext : DbContext
{
    public SearchDbContext(DbContextOptions<SearchDbContext> options)
        : base(options)
    {
    }

    public DbSet<SearchReindexState> ReindexStates => Set<SearchReindexState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("search");
        modelBuilder.MapWolverineEnvelopeStorage("search");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SearchDbContext).Assembly);
    }
}
