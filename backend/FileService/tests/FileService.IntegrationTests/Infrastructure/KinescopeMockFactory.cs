using CSharpFunctionalExtensions;
using FileService.Core.Features.Videos;
using FileService.Core.Services;
using NSubstitute;
using SharedKernel;

namespace FileService.IntegrationTests.Infrastructure;

internal static class KinescopeMockFactory
{
    public static IVideoProvider Create()
    {
        IVideoProvider mock = Substitute.For<IVideoProvider>();

        SetupInitializeUpload(mock);
        SetupStatuses(mock);
        SetupGetChapters(mock);
        SetupReplaceChapters(mock);

        return mock;
    }

    private static void SetupGetChapters(IVideoProvider mock)
    {
        mock.GetChaptersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<VideoProviderChapter>, Error>(
            [
                new VideoProviderChapter("chapter-1", "Введение", 0),
                new VideoProviderChapter("chapter-2", "Основная часть", 30),
            ]));
    }

    private static void SetupReplaceChapters(IVideoProvider mock)
    {
        mock.ReplaceChaptersAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<VideoChapterDefinition>>(),
                Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());
    }

    private static void SetupInitializeUpload(IVideoProvider mock)
    {
        mock.InitiateUploadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                string videoId = $"kinescope-video-{Guid.NewGuid():N}";
                return Result.Success<VideoUploadInitResult, Error>(
                    new VideoUploadInitResult(videoId, $"https://uploader.kinescope.io/v2/{videoId}"));
            });
    }

    private static void SetupStatuses(IVideoProvider mock)
    {
        mock.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                string videoId = callInfo.ArgAt<string>(0);
                return Result.Success<VideoProviderAssetInfo, Error>(CreateTestVideoInfo(videoId));
            });

        mock.GetStatusesBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                IReadOnlyList<string> videoIds = callInfo.ArgAt<IReadOnlyList<string>>(0);
                IReadOnlyDictionary<string, VideoProviderAssetInfo> result = videoIds.ToDictionary(
                    id => id,
                    CreateTestVideoInfo);

                return Result.Success<IReadOnlyDictionary<string, VideoProviderAssetInfo>, Error>(result);
            });
    }

    private static VideoProviderAssetInfo CreateTestVideoInfo(string videoId) =>
        new(
            ExternalAssetId: videoId,
            Title: "Test Video",
            Status: "done",
            ThumbnailUrl: $"https://kinescope.io/poster/{videoId}",
            Duration: 120,
            Width: 1920,
            Height: 1080);
}
