using System.Net;
using System.Net.Http.Json;
using Core.Database;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Videos;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class StoredSubtitlesTests(IntegrationTestsWebFactory factory) : FileServiceTestsBase(factory)
{
    [Theory]
    [InlineData("[{\"startSeconds\":1.25,\"endSeconds\":2.5,\"text\":\"Первый сегмент\"}]",
        "1\n00:00:01,250 --> 00:00:02,500\nПервый сегмент\n")]
    [InlineData("[{\"start\":1.25,\"end\":2.5,\"text\":\"Первый сегмент\"}]",
        "1\n00:00:01,250 --> 00:00:02,500\nПервый сегмент\n")]
    [InlineData("[{\"startSeconds\":90000.125,\"endSeconds\":90001.75,\"text\":\"  После суток  \"},{\"startSeconds\":null,\"endSeconds\":null,\"start\":0.001,\"end\":0.999,\"text\":\" начало \"}]",
        "1\n00:00:00,001 --> 00:00:00,999\nначало\n\n2\n25:00:00,125 --> 25:00:01,750\nПосле суток\n")]
    public async Task Owner_DownloadsExistingSegments_AsSrt(string segments, string expected)
    {
        Guid owner = Guid.CreateVersion7();
        (Guid videoId, Guid version) = await CreateReadyVideoAsync(owner);
        await StoreTranscriptAsync(videoId, version, segments);
        AuthenticateAs(owner, "platform-author");
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{videoId}/subtitles.srt/");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-subrip", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"transcript-{videoId:N}.srt", response.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        // Fixtures match the former SrtSubtitleRenderer: sort start, legacy fallback,
        // trim text, preserve milliseconds and total hours (not hours modulo 24).
        Assert.Equal(expected, (await response.Content.ReadAsStringAsync()).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Empty(OutboxCollector.Messages);
    }

    [Theory]
    [InlineData("platform-author", HttpStatusCode.Forbidden)]
    [InlineData("platform-participant", HttpStatusCode.Forbidden)]
    // The existing permission map grants platform-service administrator permissions too.
    [InlineData("platform-service", HttpStatusCode.OK)]
    [InlineData("platform-admin", HttpStatusCode.OK)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    public async Task Download_PreservesOwnerAndAdminBoundary(string role, HttpStatusCode expected)
    {
        (Guid videoId, Guid version) = await CreateReadyVideoAsync(Guid.CreateVersion7());
        await StoreTranscriptAsync(videoId, version, "[{\"startSeconds\":0,\"endSeconds\":1,\"text\":\"Private synthetic transcript\"}]");
        if (role == "anonymous")
            RemoveAuthentication();
        else if (role == "platform-admin")
            AuthenticateAsAdmin();
        else
            AuthenticateAs(Guid.CreateVersion7(), role);

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{videoId}/subtitles.srt/");

        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK)
            Assert.DoesNotContain("Private synthetic transcript", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_DoesNotReturnTranscriptForPreviousProviderVersion()
    {
        (Guid videoId, Guid version) = await CreateReadyVideoAsync(Guid.CreateVersion7());
        await StoreTranscriptAsync(videoId, version, "[{\"start\":0,\"end\":1,\"text\":\"Old version\"}]");
        await ExecuteInDb(async db =>
        {
            VideoProviderRef reference = await db.VideoProviderRefs.SingleAsync(x => x.AssetId == videoId);
            reference.UpdateMetadata(new VideoProviderMetadata { DurationSeconds = 5 });
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{videoId}/subtitles.srt/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_WithoutTranscript_ReturnsNotFoundWithoutGenerating()
    {
        (Guid videoId, _) = await CreateReadyVideoAsync(Guid.CreateVersion7());
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/videos/{videoId}/subtitles.srt/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(OutboxCollector.Messages);
    }

    [Theory]
    [InlineData("endpoint", "material_video")]
    [InlineData("endpoint", "course_video")]
    [InlineData("target", "material_video")]
    [InlineData("replacement", "material_video")]
    [InlineData("retention", "material_video")]
    [InlineData("repository-purge", "material_video")]
    public async Task VideoDeletion_CleansAllVersionsOnlyForItsAsset(string path, string usage)
    {
        (Guid videoId, Guid version) = await CreateReadyVideoAsync(Guid.CreateVersion7(), usage);
        (Guid otherId, Guid otherVersion) = await CreateReadyVideoAsync(Guid.CreateVersion7());
        Guid fileId = await CreateNonVideoAsync();
        Guid targetId = Guid.CreateVersion7();
        if (path == "target")
            await ExecuteInDb(async db =>
            {
                MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == videoId);
                Assert.True(asset.BindTo(TargetEntity.Of("material", targetId).Value).IsSuccess);
                await db.SaveChangesAsync();
            });
        if (path is "retention" or "repository-purge")
            await ExecuteInDb(async db =>
            {
                MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == videoId);
                Assert.True(asset.RequestDelete().IsSuccess);
                if (path == "repository-purge")
                    Assert.True(asset.MarkDeleted().IsSuccess);
                await db.SaveChangesAsync();
            });
        const string segments = "[{\"start\":0,\"end\":1,\"text\":\"Retained synthetic row\"}]";
        // Seed both current and older versions, including retained rows on a tombstone.
        await StoreTranscriptAsync(videoId, version, segments);
        await StoreTranscriptAsync(videoId, Guid.CreateVersion7(), segments);
        await StoreTranscriptAsync(otherId, otherVersion, segments);
        await StoreTranscriptAsync(fileId, Guid.CreateVersion7(), segments);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        switch (path)
        {
            case "endpoint":
                (await AppHttpClient.DeleteAsync($"/videos/{videoId}/")).EnsureSuccessStatusCode();
                // Repeating the existing deletion is safe.
                (await AppHttpClient.DeleteAsync($"/videos/{videoId}/")).EnsureSuccessStatusCode();
                break;
            case "target":
                AssetDeletionLifecycleService lifecycle = scope.ServiceProvider.GetRequiredService<AssetDeletionLifecycleService>();
                Assert.Equal(1, await lifecycle.DeleteByTargetEntityAsync("material", targetId, CancellationToken.None));
                await lifecycle.DeleteByTargetEntityAsync("material", targetId, CancellationToken.None);
                break;
            case "replacement":
                MediaAsset predecessor = (await scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>()
                    .GetByAsync(x => x.Id == videoId)).Value;
                Assert.True(await scope.ServiceProvider.GetRequiredService<IAssetSlotReplacement>().RequestDeleteAsync(predecessor));
                Assert.True((await scope.ServiceProvider.GetRequiredService<ITransactionManager>().SaveChangesAsync()).IsSuccess);
                break;
            case "retention":
                Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AssetRetentionService>()
                    .ProcessDeletingAssetsAsync(CancellationToken.None));
                Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AssetRetentionService>()
                    .ProcessDeletingAssetsAsync(CancellationToken.None));
                break;
            case "repository-purge":
                // Current retention deliberately keeps video provider/tombstone ownership.
                // Exercise the actual repository delete seam without changing that policy.
                await ExecuteInDb(async db =>
                {
                    db.MediaAssets.Remove(await db.MediaAssets.SingleAsync(x => x.Id == videoId));
                    await db.SaveChangesAsync();
                });
                break;
        }

        Assert.Equal(0, await CountTranscriptsAsync(videoId));
        Assert.Equal(1, await CountTranscriptsAsync(otherId));
        Assert.Equal(1, await CountTranscriptsAsync(fileId));
        Assert.Equal(HttpStatusCode.NotFound, (await AppHttpClient.GetAsync($"/videos/{videoId}/subtitles.srt/")).StatusCode);
        await ExecuteInDb(async db =>
        {
            Assert.True((await db.MediaAssets.SingleAsync(x => x.Id == fileId)).RequestDelete().IsSuccess);
            await db.SaveChangesAsync();
        });
        Assert.Equal(1, await CountTranscriptsAsync(fileId));
    }

    [Fact]
    public async Task DeleteFailure_RollsBackAssetStateAndAllTranscriptVersionsAtomically()
    {
        (Guid videoId, Guid version) = await CreateReadyVideoAsync(Guid.CreateVersion7());
        const string segments = "[{\"start\":0,\"end\":1,\"text\":\"Rollback fixture\"}]";
        await StoreTranscriptAsync(videoId, version, segments);
        await StoreTranscriptAsync(videoId, Guid.CreateVersion7(), segments);
        await ExecuteInDb(db => db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION files.fail_after_transcript_cleanup() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN RAISE EXCEPTION 'synthetic deletion failure'; END $test$;
            CREATE TRIGGER zz_fail_after_transcript_cleanup AFTER UPDATE OF status ON files.media_assets
            FOR EACH ROW WHEN (NEW.status = 'DELETING') EXECUTE FUNCTION files.fail_after_transcript_cleanup();
            """));
        try
        {
            // PostgreSQL runs this trigger after the retained cleanup trigger, then aborts.
            HttpResponseMessage deleted = await AppHttpClient.DeleteAsync($"/videos/{videoId}/");
            Assert.Equal(HttpStatusCode.InternalServerError, deleted.StatusCode);
            Assert.Equal(2, await CountTranscriptsAsync(videoId));
            await ExecuteInDb(async db => Assert.Equal(AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(x => x.Id == videoId)).Status));
            (await AppHttpClient.GetAsync($"/videos/{videoId}/subtitles.srt/")).EnsureSuccessStatusCode();
        }
        finally
        {
            await ExecuteInDb(db => db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER zz_fail_after_transcript_cleanup ON files.media_assets;
                DROP FUNCTION files.fail_after_transcript_cleanup();
                """));
        }
    }

    private Task<int> CountTranscriptsAsync(Guid assetId) => ExecuteInDb(db => db.Database
        .SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM files.video_transcripts WHERE video_asset_id = {assetId}")
        .SingleAsync());

    private Task<Guid> CreateNonVideoAsync() => ExecuteInDb(async db =>
    {
        MediaAsset asset = MediaAsset.Register(Guid.CreateVersion7(), AssetKind.FILE,
            AssetUsageType.MARKDOWN_FILE, FileName.Of("retained.md").Value,
            MediaContentType.Of("text/markdown").Value, 10, Guid.CreateVersion7(), null, true).Value;
        Assert.True(asset.MarkReady().IsSuccess);
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    });

    private async Task<(Guid VideoId, Guid Version)> CreateReadyVideoAsync(Guid owner, string usage = "material_video")
    {
        if (usage == "course_video")
            AuthenticateAsAdmin();
        else
            AuthenticateAs(owner, "platform-author");
        var request = usage == "course_video"
            ? new InitiateVideoUploadRequest("retained.mp4", "video/mp4", 1024,
                usage, TargetEntity: new TargetEntityDto("course", Guid.CreateVersion7()))
            : new InitiateVideoUploadRequest("retained.mp4", "video/mp4", 1024,
                usage, DraftId: Guid.CreateVersion7());
        HttpResponseMessage upload = await AppHttpClient.PostAsJsonAsync("/videos/uploads/", request);
        upload.EnsureSuccessStatusCode();
        Envelope<InitiateVideoUploadResponse>? envelope = await upload.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();
        Guid id = envelope!.Result!.AssetId;
        Guid version = await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == id);
            asset.MarkProcessing();
            asset.MarkReady();
            await db.SaveChangesAsync();
            return (await db.VideoProviderRefs.SingleAsync(x => x.AssetId == id)).Version;
        });
        AuthenticateAsAdmin();
        return (id, version);
    }

    private Task StoreTranscriptAsync(Guid videoId, Guid version, string segments) => ExecuteInDb(async db =>
    {
        Guid id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO files.video_transcripts
                (id, video_asset_id, asset_version, duration_seconds, language, segments_json, created_at, updated_at)
            VALUES ({id}, {videoId}, {version}, 5, 'ru', CAST({segments} AS jsonb), now(), now())
            """);
    });
}
