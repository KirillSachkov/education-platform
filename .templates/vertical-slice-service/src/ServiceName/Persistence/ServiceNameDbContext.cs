using ServiceName.Domain.Widgets;

namespace ServiceName.Persistence;

public sealed class ServiceNameDbContext : DbContext
{
    public ServiceNameDbContext(DbContextOptions<ServiceNameDbContext> options) : base(options) { }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("__SERVICE_SCHEMA__");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceNameDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
