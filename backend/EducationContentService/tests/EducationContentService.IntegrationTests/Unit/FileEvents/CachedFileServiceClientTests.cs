using CSharpFunctionalExtensions;
using EducationContentService.Core.Features.FileEvents;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Unit.FileEvents;

public sealed class CachedFileServiceClientTests
{
    [Fact]
    public async Task GetFilesBatchAsync_ChunksCacheMissesByFifty()
    {
        Guid[] ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();
        IFileServiceClient inner = Substitute.For<IFileServiceClient>();
        inner.GetFilesBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<List<GetFileResponse>?, Error>(
                call.ArgAt<IReadOnlyList<Guid>>(0)
                    .Select(id => new GetFileResponse(
                        id, "file", "cover", "ready", "cover.jpg", "image/jpeg", 1,
                        $"https://cdn/{id}", null, false))
                    .ToList()));

        ServiceCollection services = new();
        services.AddMemoryCache();
        services.AddHybridCache();
        using ServiceProvider provider = services.BuildServiceProvider();
        var sut = new CachedFileServiceClient(inner, provider.GetRequiredService<HybridCache>());

        Result<List<GetFileResponse>?, Error> result =
            await sut.GetFilesBatchAsync(ids, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ids, result.Value!.Select(x => x.Id));
        Assert.Equal(2, inner.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IFileServiceClient.GetFilesBatchAsync)));
        await inner.Received(1).GetFilesBatchAsync(
            Arg.Is<IReadOnlyList<Guid>>(chunk => chunk.Count == 50), Arg.Any<CancellationToken>());
        await inner.Received(1).GetFilesBatchAsync(
            Arg.Is<IReadOnlyList<Guid>>(chunk => chunk.Count == 1), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicBatchAndPrivilegedDetail_UseIndependentCacheEntries(bool publicFirst)
    {
        Guid assetId = Guid.NewGuid();
        var publicVideo = new GetPublicVideoResponse(assetId, "https://cdn/thumb.jpg", 120);
        var detailVideo = new GetVideoResponse(
            assetId,
            "video",
            "material_video",
            "ready",
            "private-name.mp4",
            "video/mp4",
            1024,
            null,
            false,
            "provider-secret-id",
            publicVideo.ThumbnailUrl,
            publicVideo.DurationSeconds);

        IFileServiceClient inner = Substitute.For<IFileServiceClient>();
        inner.GetVideosBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<GetPublicVideoResponse>?, Error>([publicVideo]));
        inner.GetVideoAsync(assetId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetVideoResponse?, Error>(detailVideo));

        ServiceCollection services = new();
        services.AddMemoryCache();
        services.AddHybridCache();
        using ServiceProvider provider = services.BuildServiceProvider();
        var sut = new CachedFileServiceClient(inner, provider.GetRequiredService<HybridCache>());

        if (publicFirst)
        {
            await sut.GetVideosBatchAsync([assetId], CancellationToken.None);
            await sut.GetVideoAsync(assetId, CancellationToken.None);
        }
        else
        {
            await sut.GetVideoAsync(assetId, CancellationToken.None);
            await sut.GetVideosBatchAsync([assetId], CancellationToken.None);
        }

        Result<GetVideoResponse?, Error> detail =
            await sut.GetVideoAsync(assetId, CancellationToken.None);
        Result<List<GetPublicVideoResponse>?, Error> publicResult =
            await sut.GetVideosBatchAsync([assetId], CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Equal("provider-secret-id", detail.Value!.ExternalVideoId);
        Assert.True(publicResult.IsSuccess);
        Assert.Equal(publicVideo, Assert.Single(publicResult.Value!));

        await inner.Received(1).GetVideoAsync(assetId, Arg.Any<CancellationToken>());
        await inner.Received(1).GetVideosBatchAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { assetId })),
            Arg.Any<CancellationToken>());
    }
}
