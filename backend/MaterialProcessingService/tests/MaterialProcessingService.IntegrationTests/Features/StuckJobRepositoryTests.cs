using Microsoft.EntityFrameworkCore;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;
using MaterialProcessingService.Infrastructure.Postgres.Repositories;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

public sealed class StuckJobRepositoryTests : MaterialProcessingServiceTestsBase
{
    public StuckJobRepositoryTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetStuckJobs_UsesLastUpdateHeartbeat_NotOriginalStartTime()
    {
        Guid healthyTimecodeId = Guid.CreateVersion7();
        Guid staleTimecodeId = Guid.CreateVersion7();
        Guid healthyContentId = Guid.CreateVersion7();
        Guid staleContentId = Guid.CreateVersion7();

        await ExecuteInDb(async dbContext =>
        {
            ProcessingSourceType sourceType = ProcessingSourceType.Create("HLS").Value;
            TimecodeGenerationJob healthyTimecode = CreateTimecodeJob(sourceType);
            TimecodeGenerationJob staleTimecode = CreateTimecodeJob(sourceType);
            ContentGenerationJob healthyContent = CreateContentJob(sourceType);
            ContentGenerationJob staleContent = CreateContentJob(sourceType);

            healthyTimecode.Start();
            staleTimecode.Start();
            healthyContent.Start();
            staleContent.Start();

            dbContext.TimecodeGenerationJobs.AddRange(healthyTimecode, staleTimecode);
            dbContext.ContentGenerationJobs.AddRange(healthyContent, staleContent);
            await dbContext.SaveChangesAsync();

            healthyTimecodeId = healthyTimecode.Id;
            staleTimecodeId = staleTimecode.Id;
            healthyContentId = healthyContent.Id;
            staleContentId = staleContent.Id;

            DateTime oldStart = DateTime.UtcNow.AddHours(-2);
            DateTime freshHeartbeat = DateTime.UtcNow.AddMinutes(-5);
            DateTime staleHeartbeat = DateTime.UtcNow.AddHours(-2);

            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE material_processing.timecode_generation_jobs
                SET started_at = {oldStart},
                    updated_at = CASE WHEN id = {healthyTimecodeId} THEN {freshHeartbeat} ELSE {staleHeartbeat} END
                WHERE id IN ({healthyTimecodeId}, {staleTimecodeId});

                UPDATE material_processing.content_generation_jobs
                SET started_at = {oldStart},
                    updated_at = CASE WHEN id = {healthyContentId} THEN {freshHeartbeat} ELSE {staleHeartbeat} END
                WHERE id IN ({healthyContentId}, {staleContentId});
                """);

            dbContext.ChangeTracker.Clear();

            var timecodeRepository = new TimecodeGenerationJobRepository(dbContext);
            var contentRepository = new ContentGenerationJobRepository(dbContext);
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-45);

            IReadOnlyList<TimecodeGenerationJob> stuckTimecodes =
                await timecodeRepository.GetStuckJobsAsync(cutoff, 50);
            IReadOnlyList<ContentGenerationJob> stuckContent =
                await contentRepository.GetStuckJobsAsync(cutoff, 50);

            Assert.Contains(stuckTimecodes, job => job.Id == staleTimecodeId);
            Assert.DoesNotContain(stuckTimecodes, job => job.Id == healthyTimecodeId);
            Assert.Contains(stuckContent, job => job.Id == staleContentId);
            Assert.DoesNotContain(stuckContent, job => job.Id == healthyContentId);
        });
    }

    private static TimecodeGenerationJob CreateTimecodeJob(ProcessingSourceType sourceType) =>
        TimecodeGenerationJob.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sourceType).Value;

    private static ContentGenerationJob CreateContentJob(ProcessingSourceType sourceType) =>
        ContentGenerationJob.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sourceType).Value;
}
