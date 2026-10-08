using Microsoft.EntityFrameworkCore;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;
using MaterialProcessingService.Domain.Transcripts;
using Wolverine.EntityFrameworkCore;

namespace MaterialProcessingService.Infrastructure.Postgres;

public sealed class MaterialProcessingServiceDbContext : DbContext
{
    public MaterialProcessingServiceDbContext(DbContextOptions<MaterialProcessingServiceDbContext> options)
        : base(options)
    {
    }

    public DbSet<TimecodeGenerationJob> TimecodeGenerationJobs => Set<TimecodeGenerationJob>();

    public DbSet<ContentGenerationJob> ContentGenerationJobs => Set<ContentGenerationJob>();

    public DbSet<VideoTranscript> VideoTranscripts => Set<VideoTranscript>();

    public DbSet<AiModelSettings> AiModelSettings => Set<AiModelSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("material_processing");
        modelBuilder.MapWolverineEnvelopeStorage("material_processing");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MaterialProcessingServiceDbContext).Assembly);
    }
}
