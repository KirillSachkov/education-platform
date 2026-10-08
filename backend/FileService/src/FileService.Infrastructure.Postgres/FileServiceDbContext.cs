using FileService.Domain;
using Wolverine.EntityFrameworkCore;

namespace FileService.Infrastructure.Postgres;

public class FileServiceDbContext : DbContext
{
    public FileServiceDbContext(DbContextOptions<FileServiceDbContext> options)
        : base(options)
    {
    }

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<FileStorageRef> FileStorageRefs => Set<FileStorageRef>();
    public DbSet<VideoProviderRef> VideoProviderRefs => Set<VideoProviderRef>();
    public DbSet<AssetOwnershipCheckpoint> AssetOwnershipCheckpoints => Set<AssetOwnershipCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("files");
        modelBuilder.HasSequence<long>("asset_binding_revision_seq", "files")
            .StartsAt(1L);

        modelBuilder.MapWolverineEnvelopeStorage("files");

        // #646: ImageVariant is persisted as a JSONB value (whole-list converter on
        // MediaAsset._imageVariants), NOT a relational entity — keep EF from discovering
        // it as one via the List<ImageVariant> property.
        modelBuilder.Ignore<ImageVariant>();

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FileServiceDbContext).Assembly);
    }
}
