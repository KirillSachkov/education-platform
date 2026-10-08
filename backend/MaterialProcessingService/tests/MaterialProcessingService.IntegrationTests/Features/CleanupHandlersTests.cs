using Microsoft.EntityFrameworkCore;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CleanupHandlersTests : MaterialProcessingServiceTestsBase
{
    public CleanupHandlersTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task FileAssetDeleted_ForVideo_RemovesTranscriptAndJobs()
    {
        Guid videoId = Guid.CreateVersion7();
        await SeedTranscriptAndJobsAsync(videoId, Guid.CreateVersion7());

        await InvokeMessageAndWaitAsync(new FileAssetDeleted(
            AssetId: videoId,
            Kind: "video",
            UsageType: FileEventsRouting.UsageTypes.MATERIAL_VIDEO,
            TargetEntityId: Guid.CreateVersion7(),
            TargetEntityType: FileEventsRouting.EntityTypes.MATERIAL));

        await ExecuteInDb(async db =>
        {
            Assert.False(await db.VideoTranscripts.AnyAsync(t => t.VideoAssetId == videoId));
            Assert.False(await db.TimecodeGenerationJobs.AnyAsync(j => j.VideoAssetId == videoId));
            Assert.False(await db.ContentGenerationJobs.AnyAsync(j => j.VideoAssetId == videoId));
        });
    }

    [Fact]
    public async Task FileAssetDeleted_ForCover_DoesNotTouchTranscripts()
    {
        Guid videoId = Guid.CreateVersion7();
        await SeedTranscriptAndJobsAsync(videoId, Guid.CreateVersion7());

        // Не video usage type — handler должен no-op'нуть.
        await InvokeMessageAndWaitAsync(new FileAssetDeleted(
            AssetId: videoId,
            Kind: "image",
            UsageType: FileEventsRouting.UsageTypes.MATERIAL_PREVIEW,
            TargetEntityId: Guid.CreateVersion7(),
            TargetEntityType: FileEventsRouting.EntityTypes.MATERIAL));

        await ExecuteInDb(async db =>
        {
            Assert.True(await db.VideoTranscripts.AnyAsync(t => t.VideoAssetId == videoId));
        });
    }

    [Fact]
    public async Task MaterialHardDeleted_RemovesContentJobs_KeepsTranscriptAndTimecodeJobs()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        await SeedTranscriptAndJobsAsync(videoId, materialId);

        await InvokeMessageAndWaitAsync(new MaterialHardDeleted(materialId));

        await ExecuteInDb(async db =>
        {
            Assert.False(await db.ContentGenerationJobs.AnyAsync(j => j.MaterialId == materialId));
            Assert.True(await db.TimecodeGenerationJobs.AnyAsync(j => j.VideoAssetId == videoId));
            Assert.True(await db.VideoTranscripts.AnyAsync(t => t.VideoAssetId == videoId));
        });
    }

    private async Task SeedTranscriptAndJobsAsync(Guid videoId, Guid materialId)
    {
        Guid assetVersion = Guid.CreateVersion7();
        Guid requestedBy = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into material_processing.video_transcripts
                  (id, video_asset_id, asset_version, duration_seconds, language, segments_json)
                values
                  ({Guid.CreateVersion7()}, {videoId}, {assetVersion}, 60, 'ru', '[]'::jsonb);

                insert into material_processing.timecode_generation_jobs
                  (id, video_asset_id, asset_version, status, stage, progress_percent,
                   mode, source_type, requested_by_user_id, model_override, trigger_source)
                values
                  ({Guid.CreateVersion7()}, {videoId}, {assetVersion}, 'COMPLETED', 'SAVE', 100,
                   'TIMECODES', 'KINESCOPE', {requestedBy}, null, 'MANUAL');

                insert into material_processing.content_generation_jobs
                  (id, video_asset_id, material_id, asset_version, status, stage, progress_percent,
                   source_type, requested_by_user_id, model_override)
                values
                  ({Guid.CreateVersion7()}, {videoId}, {materialId}, {assetVersion}, 'COMPLETED',
                   'SAVE', 100, 'KINESCOPE', {requestedBy}, null);
                """);
            await db.SaveChangesAsync();
        });
    }
}
