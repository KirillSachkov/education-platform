using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.IntegrationTests.Unit;

public class MarkdownContentTests
{
    [Fact]
    public void Create_WithMaxLengthValue_Succeeds()
    {
        string value = new('a', MarkdownContent.MAX_LENGTH);

        var result = MarkdownContent.Create(value);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_WhenValueExceedsMaxLength_Fails()
    {
        string value = new('a', MarkdownContent.MAX_LENGTH + 1);

        var result = MarkdownContent.Create(value);

        Assert.True(result.IsFailure);
    }
}
