using FileService.Domain;

namespace FileService.UnitTests.Domain;

public class AssetUsagePolicyTests
{
    [Theory]
    [InlineData("avatar.jpg", "image/jpeg")]
    [InlineData("avatar.png", "image/png")]
    [InlineData("avatar.webp", "image/webp")]
    public void ValidateUpload_Avatar_ValidFile_ReturnsSuccess(string fileName, string contentType)
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var name = FileName.Of(fileName).Value;
        var ct = MediaContentType.Of(contentType).Value;

        var result = policy.ValidateUpload(name, ct, 1024);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("document.pdf", "application/pdf")]
    [InlineData("notes.txt", "text/plain")]
    public void ValidateUpload_MarkdownFile_ValidFile_ReturnsSuccess(string fileName, string contentType)
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.MARKDOWN_FILE).Value;
        var name = FileName.Of(fileName).Value;
        var ct = MediaContentType.Of(contentType).Value;

        var result = policy.ValidateUpload(name, ct, 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_SizeZero_ReturnsFailure()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var name = FileName.Of("test.jpg").Value;
        var ct = MediaContentType.Of("image/jpeg").Value;

        var result = policy.ValidateUpload(name, ct, 0);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateUpload_SizeOverLimit_ReturnsFailure()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var name = FileName.Of("test.jpg").Value;
        var ct = MediaContentType.Of("image/jpeg").Value;
        long overLimit = (5 * 1024 * 1024) + 1;

        var result = policy.ValidateUpload(name, ct, overLimit);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateUpload_WrongMimeType_ReturnsFailure()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var name = FileName.Of("test.gif").Value;
        var ct = MediaContentType.Of("image/gif").Value;

        var result = policy.ValidateUpload(name, ct, 1024);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateUpload_ExtensionMismatch_ReturnsFailure()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var name = FileName.Of("photo.png").Value;
        var ct = MediaContentType.Of("image/jpeg").Value;

        var result = policy.ValidateUpload(name, ct, 1024);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateUpload_JpegExtensionWithJpegContentType_ReturnsSuccess()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var name = FileName.Of("photo.jpeg").Value;
        var ct = MediaContentType.Of("image/jpeg").Value;

        var result = policy.ValidateUpload(name, ct, 1024);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    public void GetCanonicalExtension_Avatar_KnownMime_ReturnsExpected(string mime, string expected)
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var ct = MediaContentType.Of(mime).Value;

        var result = policy.GetCanonicalExtension(ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void GetCanonicalExtension_UnknownMime_ReturnsFailure()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;
        var ct = MediaContentType.Of("image/gif").Value;

        var result = policy.GetCanonicalExtension(ct);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void IsTargetTypeAllowed_AllowedType_ReturnsTrue()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;

        Assert.True(policy.IsTargetTypeAllowed("user"));
        Assert.True(policy.IsTargetTypeAllowed("profile"));
    }

    [Fact]
    public void IsTargetTypeAllowed_DisallowedType_ReturnsFalse()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;

        Assert.False(policy.IsTargetTypeAllowed("course"));
    }

    [Theory]
    [InlineData(AssetUsageType.MARKDOWN_IMAGE)]
    [InlineData(AssetUsageType.MARKDOWN_FILE)]
    public void IsTargetTypeAllowed_MarkdownAssets_AllowProjectMaterialAndIssue(AssetUsageType usageType)
    {
        var policy = AssetUsagePolicyCatalog.Get(usageType).Value;

        Assert.True(policy.IsTargetTypeAllowed("project"));
        Assert.True(policy.IsTargetTypeAllowed("material"));
        Assert.True(policy.IsTargetTypeAllowed("issue"));
    }

    [Fact]
    public void IsTargetTypeAllowed_CaseInsensitive_ReturnsTrue()
    {
        var policy = AssetUsagePolicyCatalog.Get(AssetUsageType.AVATAR).Value;

        Assert.True(policy.IsTargetTypeAllowed("User"));
        Assert.True(policy.IsTargetTypeAllowed("USER"));
    }

    [Theory]
    [InlineData(AssetUsageType.AVATAR, AssetKind.FILE)]
    [InlineData(AssetUsageType.COURSE_PREVIEW, AssetKind.FILE)]
    [InlineData(AssetUsageType.MARKDOWN_IMAGE, AssetKind.FILE)]
    [InlineData(AssetUsageType.MARKDOWN_FILE, AssetKind.FILE)]
    [InlineData(AssetUsageType.MATERIAL_VIDEO, AssetKind.VIDEO)]
    [InlineData(AssetUsageType.COURSE_VIDEO, AssetKind.VIDEO)]
    [InlineData(AssetUsageType.MATERIAL_PREVIEW, AssetKind.FILE)]
    public void Get_ValidUsageType_ReturnsSuccessWithCorrectKind(AssetUsageType usageType, AssetKind expectedKind)
    {
        var result = AssetUsagePolicyCatalog.Get(usageType);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedKind, result.Value.Kind);
    }

    [Fact]
    public void Get_InvalidUsageType_ReturnsFailure()
    {
        var result = AssetUsagePolicyCatalog.Get((AssetUsageType)(-1));

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(AssetUsageType.AVATAR)]
    [InlineData(AssetUsageType.COURSE_PREVIEW)]
    [InlineData(AssetUsageType.COURSE_VIDEO)]
    public void RequiresDraft_EntityRequired_ReturnsFalse(AssetUsageType usageType)
    {
        var policy = AssetUsagePolicyCatalog.Get(usageType).Value;

        Assert.False(policy.RequiresDraft);
    }

    [Theory]
    [InlineData(AssetUsageType.MARKDOWN_IMAGE)]
    [InlineData(AssetUsageType.MARKDOWN_FILE)]
    [InlineData(AssetUsageType.MATERIAL_PREVIEW)]
    [InlineData(AssetUsageType.MATERIAL_VIDEO)]
    public void RequiresDraft_DraftOrEntity_ReturnsTrue(AssetUsageType usageType)
    {
        var policy = AssetUsagePolicyCatalog.Get(usageType).Value;

        Assert.True(policy.RequiresDraft);
    }
}
