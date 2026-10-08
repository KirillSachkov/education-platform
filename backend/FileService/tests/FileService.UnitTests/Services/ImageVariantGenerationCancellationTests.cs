using System.Linq.Expressions;
using Core.Database;
using CSharpFunctionalExtensions;
using FileService.Core.FilesStorage;
using FileService.Core.Imaging;
using FileService.Core.Repositories;
using FileService.Core.Services.Files;
using FileService.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel;

namespace FileService.UnitTests.Services;

public sealed class ImageVariantGenerationCancellationTests
{
    [Theory]
    [InlineData("render")]
    [InlineData("upload")]
    [InlineData("read-width")]
    public async Task GenerateAsync_CancelledAfterSuccessfulOperation_DoesNotRecordOrSaveVariants(string cancellationStage)
    {
        using var cancellation = new CancellationTokenSource();
        MediaAsset asset = MediaAsset.Register(
            Guid.NewGuid(), AssetKind.FILE, AssetUsageType.MARKDOWN_IMAGE,
            FileName.Of("pic.png").Value, MediaContentType.Of("image/png").Value,
            1024, Guid.NewGuid(), null, true).Value;
        Assert.True(asset.MarkReady().IsSuccess);
        var originalVariant = new ImageVariant(320, "existing-variant.webp", "image/webp", 3);
        asset.SetImageVariants([originalVariant]);
        FileStorageRef storageRef = FileStorageRef.Create(asset.Id, StorageKey.Of("original.png").Value).Value;
        IMediaAssetRepository assets = Substitute.For<IMediaAssetRepository>();
        IFileStorageRefRepository references = Substitute.For<IFileStorageRefRepository>();
        IObjectStorageProvider storage = Substitute.For<IObjectStorageProvider>();
        IImageVariantRenderer renderer = Substitute.For<IImageVariantRenderer>();
        ITransactionManager transaction = Substitute.For<ITransactionManager>();
        assets.GetByAsync(Arg.Any<Expression<Func<MediaAsset, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<MediaAsset, Error>(asset));
        references.GetByAsync(Arg.Any<Expression<Func<FileStorageRef, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<FileStorageRef, Error>(storageRef));
        storage.DownloadObjectAsync("original.png", Arg.Any<CancellationToken>())
            .Returns(Result.Success<byte[], Error>([1, 2, 3]));
        renderer.ReadWidth(Arg.Any<byte[]>()).Returns(_ =>
        {
            if (cancellationStage == "read-width")
            {
                cancellation.Cancel();
                return Result.Success<int, Error>(200);
            }

            return Result.Success<int, Error>(640);
        });
        renderer.RenderWebpAsync(Arg.Any<byte[]>(), 320, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (cancellationStage == "render")
                {
                    await cancellation.CancelAsync();
                }

                return Result.Success<RenderedImage, Error>(
                    new RenderedImage(320, 192, [4, 5, 6], "image/webp"));
            });
        storage.UploadObjectAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (cancellationStage == "upload")
                {
                    await cancellation.CancelAsync();
                }

                return UnitResult.Success<Error>();
            });
        var generation = new ImageVariantGenerationService(
            assets, references, storage, renderer, transaction, NullLogger<ImageVariantGenerationService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation.GenerateAsync(asset.Id, cancellation.Token));

        Assert.Same(originalVariant, Assert.Single(asset.ImageVariants));
        await transaction.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await storage.Received(cancellationStage == "upload" ? 1 : 0).UploadObjectAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}