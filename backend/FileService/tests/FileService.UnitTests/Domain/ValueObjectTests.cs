using FileService.Domain;

namespace FileService.UnitTests.Domain;

public class FileNameTests
{
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("image.PNG")]
    [InlineData("my document (1).docx")]
    [InlineData("noextension")]
    public void Of_ValidName_ReturnsSuccess(string name)
    {
        var result = FileName.Of(name);

        Assert.True(result.IsSuccess);
        Assert.Equal(name.Trim(), result.Value.Value);
    }

    [Fact]
    public void Of_TrimsWhitespace()
    {
        var result = FileName.Of("  report.pdf  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("report.pdf", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Of_EmptyOrWhitespace_ReturnsFailure(string? name)
    {
        var result = FileName.Of(name!);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Of_TooLong_ReturnsFailure()
    {
        string longName = new('a', 501);

        var result = FileName.Of(longName);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Of_ExactlyMaxLength_ReturnsSuccess()
    {
        string name = new('a', 500);

        var result = FileName.Of(name);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("file\0name")]
    [InlineData("file/name")]
    public void Of_InvalidFileNameChars_ReturnsFailure(string name)
    {
        var result = FileName.Of(name);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("photo.JPG", "jpg")]
    [InlineData("document.pdf", "pdf")]
    [InlineData("archive.tar.gz", "gz")]
    [InlineData("noextension", "")]
    public void GetExtension_ReturnsLowercasedWithoutDot(string name, string expected)
    {
        var fileName = FileName.Of(name).Value;

        string extension = fileName.GetExtension();

        Assert.Equal(expected, extension);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var fileName = FileName.Of("test.txt").Value;

        Assert.Equal("test.txt", fileName.ToString());
    }
}

public class StorageKeyTests
{
    [Theory]
    [InlineData("files/abc.png")]
    [InlineData("images/photo.jpg")]
    [InlineData("a/b/c/d")]
    public void Of_ValidKey_ReturnsSuccess(string key)
    {
        var result = StorageKey.Of(key);

        Assert.True(result.IsSuccess);
        Assert.Equal(key, result.Value.Value);
    }

    [Fact]
    public void Of_NormalizesBackslashes()
    {
        var result = StorageKey.Of(@"images\photo.jpg");

        Assert.True(result.IsSuccess);
        Assert.Equal("images/photo.jpg", result.Value.Value);
    }

    [Fact]
    public void Of_TrimsSlashes()
    {
        var result = StorageKey.Of("/images/photo.jpg/");

        Assert.True(result.IsSuccess);
        Assert.Equal("images/photo.jpg", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Of_EmptyOrWhitespace_ReturnsFailure(string? key)
    {
        var result = StorageKey.Of(key!);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Of_TooLong_ReturnsFailure()
    {
        string longKey = string.Join("/", Enumerable.Repeat("segment", 100));
        Assert.True(longKey.Length > 500);

        var result = StorageKey.Of(longKey);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("files/../secret")]
    [InlineData("./hidden")]
    [InlineData("a/b/../c")]
    [InlineData("a/./b")]
    public void Of_PathTraversal_ReturnsFailure(string key)
    {
        var result = StorageKey.Of(key);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ForFile_ValidInputs_ReturnsCorrectFormat()
    {
        var assetId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        var result = StorageKey.ForFile(assetId, "png");

        Assert.True(result.IsSuccess);
        Assert.Equal($"files/{assetId:N}.png", result.Value.Value);
    }

    [Fact]
    public void ForFile_NormalizesExtension()
    {
        var assetId = Guid.NewGuid();

        var result = StorageKey.ForFile(assetId, ".PNG");

        Assert.True(result.IsSuccess);
        Assert.Equal($"files/{assetId:N}.png", result.Value.Value);
    }

    [Fact]
    public void ForFile_EmptyGuid_ReturnsFailure()
    {
        var result = StorageKey.ForFile(Guid.Empty, "png");

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void ForFile_EmptyExtension_ReturnsFailure(string? extension)
    {
        var result = StorageKey.ForFile(Guid.NewGuid(), extension!);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var key = StorageKey.Of("files/test.png").Value;

        Assert.Equal("files/test.png", key.ToString());
    }
}

public class MediaContentTypeTests
{
    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData("IMAGE/PNG", "image/png")]
    [InlineData("  text/plain  ", "text/plain")]
    public void Of_ValidContentType_ReturnsNormalized(string input, string expected)
    {
        var result = MediaContentType.Of(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Of_EmptyOrWhitespace_ReturnsFailure(string? value)
    {
        var result = MediaContentType.Of(value!);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("imagepng")]
    [InlineData("plaintext")]
    public void Of_NoSlash_ReturnsFailure(string value)
    {
        var result = MediaContentType.Of(value);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("image/png/extra")]
    [InlineData("a/b/c")]
    public void Of_MultipleSlashes_ReturnsFailure(string value)
    {
        var result = MediaContentType.Of(value);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void StartsWith_MatchingPrefix_ReturnsTrue()
    {
        var contentType = MediaContentType.Of("image/png").Value;

        Assert.True(contentType.StartsWith("image/"));
    }

    [Fact]
    public void StartsWith_NonMatchingPrefix_ReturnsFalse()
    {
        var contentType = MediaContentType.Of("application/pdf").Value;

        Assert.False(contentType.StartsWith("image/"));
    }

    [Fact]
    public void StartsWith_IsCaseInsensitive()
    {
        var contentType = MediaContentType.Of("image/png").Value;

        Assert.True(contentType.StartsWith("IMAGE/"));
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var contentType = MediaContentType.Of("image/jpeg").Value;

        Assert.Equal("image/jpeg", contentType.ToString());
    }
}

public class TargetEntityTests
{
    [Fact]
    public void Of_ValidInputs_ReturnsSuccess()
    {
        var id = Guid.NewGuid();

        var result = TargetEntity.Of("Material", id);

        Assert.True(result.IsSuccess);
        Assert.Equal("material", result.Value.Type);
        Assert.Equal(id, result.Value.Id);
    }

    [Fact]
    public void Of_NormalizesType_ToLowerTrimmed()
    {
        var id = Guid.NewGuid();

        var result = TargetEntity.Of("  Course  ", id);

        Assert.True(result.IsSuccess);
        Assert.Equal("course", result.Value.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Of_EmptyType_ReturnsFailure(string? type)
    {
        var result = TargetEntity.Of(type!, Guid.NewGuid());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Of_TypeTooLong_ReturnsFailure()
    {
        string longType = new('a', 101);

        var result = TargetEntity.Of(longType, Guid.NewGuid());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Of_TypeExactlyMaxLength_ReturnsSuccess()
    {
        string type = new('a', 100);

        var result = TargetEntity.Of(type, Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Of_EmptyGuid_ReturnsFailure()
    {
        var result = TargetEntity.Of("material", Guid.Empty);

        Assert.True(result.IsFailure);
    }
}
