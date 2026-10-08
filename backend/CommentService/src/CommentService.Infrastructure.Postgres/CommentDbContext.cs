using CommentService.Domain;
using Wolverine.EntityFrameworkCore;

namespace CommentService.Infrastructure.Postgres;

public class CommentDbContext : DbContext
{
    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<AuthorFeedState> AuthorFeedStates => Set<AuthorFeedState>();

    public CommentDbContext(DbContextOptions<CommentDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("comments");
        // Wolverine envelope tables — те же, что и domain, schema "comments".
        modelBuilder.MapWolverineEnvelopeStorage("comments");
        modelBuilder.HasPostgresExtension("ltree");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommentDbContext).Assembly);
    }
}
