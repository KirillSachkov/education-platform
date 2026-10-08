using System.Net.Http.Json;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

public sealed class ArtifactStatusAuthorizationTests : MaterialProcessingServiceTestsBase
{
    public ArtifactStatusAuthorizationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task BatchStatuses_ReturnsOnlyArtifactsRequestedByCurrentAuthor()
    {
        Guid ownerUserId = Guid.CreateVersion7();
        Guid otherAuthorId = Guid.CreateVersion7();
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        ProcessingSourceType sourceType = ProcessingSourceType.Create("HLS").Value;

        await ExecuteInDb(async dbContext =>
        {
            TimecodeGenerationJob timecodeJob = TimecodeGenerationJob.Create(
                videoId,
                Guid.CreateVersion7(),
                ownerUserId,
                sourceType).Value;
            timecodeJob.MarkCompleted();

            ContentGenerationJob contentJob = ContentGenerationJob.Create(
                videoId,
                materialId,
                Guid.CreateVersion7(),
                ownerUserId,
                sourceType).Value;
            contentJob.MarkCompleted();

            dbContext.TimecodeGenerationJobs.Add(timecodeJob);
            dbContext.ContentGenerationJobs.Add(contentJob);
            await dbContext.SaveChangesAsync();
        });

        var request = new GetVideoArtifactStatusesRequest(
            [new VideoArtifactQuery(videoId, materialId)]);

        AuthenticateAs(otherAuthorId, "platform-author");
        HttpResponseMessage otherResponse = await AppHttpClient.PostAsJsonAsync(
            "/material-processing/artifacts/by-videos/",
            request);
        otherResponse.EnsureSuccessStatusCode();
        GetVideoArtifactStatusesResponse otherPayload =
            await ReadResultAsync<GetVideoArtifactStatusesResponse>(otherResponse);

        Assert.False(otherPayload.Items.Single().HasTimecodes);
        Assert.False(otherPayload.Items.Single().HasSummary);

        AuthenticateAs(ownerUserId, "platform-author");
        HttpResponseMessage ownerResponse = await AppHttpClient.PostAsJsonAsync(
            "/material-processing/artifacts/by-videos/",
            request);
        ownerResponse.EnsureSuccessStatusCode();
        GetVideoArtifactStatusesResponse ownerPayload =
            await ReadResultAsync<GetVideoArtifactStatusesResponse>(ownerResponse);

        Assert.True(ownerPayload.Items.Single().HasTimecodes);
        Assert.True(ownerPayload.Items.Single().HasSummary);
    }
}
