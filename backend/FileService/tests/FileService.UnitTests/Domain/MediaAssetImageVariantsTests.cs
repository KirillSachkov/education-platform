using CSharpFunctionalExtensions;
using FileService.Domain;
using SharedKernel;

namespace FileService.UnitTests.Domain;

public class MediaAssetImageVariantsTests
{
    private static MediaAsset ReadyImageAsset(string contentType = "image/png")
    {
        Result<MediaAsset, Error> result = MediaAsset.Register(
            id: Guid.NewGuid(),
            kind: AssetKind.FILE,
            usageType: AssetUsageType.MARKDOWN_IMAGE,
            fileName: FileName.Of("pic.png").Value,
            contentType: MediaContentType.Of(contentType).Value,
            size: 1024,
            draftId: Guid.NewGuid(),
            targetEntity: null,
            isTemporary: true);

        MediaAsset asset = result.Value;
        asset.MarkReady();
        return asset;
    }

    [Fact]
    public void IsImageVariantEligible_ReadyImageFile_True()
    {
        Assert.True(ReadyImageAsset().IsImageVariantEligible());
    }

    [Fact]
    public void IsImageVariantEligible_PendingImage_False()
    {
        Result<MediaAsset, Error> result = MediaAsset.Register(
            Guid.NewGuid(), AssetKind.FILE, AssetUsageType.MARKDOWN_IMAGE,
            FileName.Of("pic.png").Value, MediaContentType.Of("image/png").Value,
            1024, Guid.NewGuid(), null, true);

        // Still PENDING_UPLOAD — not eligible until Ready.
        Assert.False(result.Value.IsImageVariantEligible());
    }

    [Fact]
    public void IsImageVariantEligible_NonImageContentType_False()
    {
        // A markdown_file PDF is a READY file but not an image.
        Result<MediaAsset, Error> result = MediaAsset.Register(
            Guid.NewGuid(), AssetKind.FILE, AssetUsageType.MARKDOWN_FILE,
            FileName.Of("doc.pdf").Value, MediaContentType.Of("application/pdf").Value,
            1024, Guid.NewGuid(), null, true);
        result.Value.MarkReady();

        Assert.False(result.Value.IsImageVariantEligible());
    }

    [Fact]
    public void SetImageVariants_RecordsSortedByWidth()
    {
        MediaAsset asset = ReadyImageAsset();

        asset.SetImageVariants(
        [
            new ImageVariant(960, "k960", "image/webp", 30),
            new ImageVariant(320, "k320", "image/webp", 10),
            new ImageVariant(640, "k640", "image/webp", 20),
        ]);

        Assert.True(asset.HasImageVariants());
        Assert.Equal(new[] { 320, 640, 960 }, asset.ImageVariants.Select(v => v.Width));
    }

    [Fact]
    public void SetImageVariants_IsIdempotentReplace()
    {
        MediaAsset asset = ReadyImageAsset();
        asset.SetImageVariants([new ImageVariant(320, "old", "image/webp", 10)]);
        asset.SetImageVariants([new ImageVariant(320, "new", "image/webp", 11), new ImageVariant(640, "k640", "image/webp", 20)]);

        Assert.Equal(2, asset.ImageVariants.Count);
        Assert.Equal("new", asset.ImageVariants[0].StorageKey);
    }

    [Fact]
    public void SelectVariantForWidth_DelegatesToPolicy()
    {
        MediaAsset asset = ReadyImageAsset();
        asset.SetImageVariants(
        [
            new ImageVariant(320, "k320", "image/webp", 10),
            new ImageVariant(640, "k640", "image/webp", 20),
        ]);

        Assert.Equal(640, asset.SelectVariantForWidth(500)!.Width);
        Assert.Null(asset.SelectVariantForWidth(null));   // no width → original
        Assert.Null(asset.SelectVariantForWidth(1280));   // above largest variant → original
    }

    [Fact]
    public void ClearImageVariants_Empties()
    {
        MediaAsset asset = ReadyImageAsset();
        asset.SetImageVariants([new ImageVariant(320, "k", "image/webp", 10)]);
        asset.ClearImageVariants();
        Assert.False(asset.HasImageVariants());
    }
}
