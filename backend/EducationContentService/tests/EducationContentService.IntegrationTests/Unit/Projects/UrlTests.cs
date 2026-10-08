using CSharpFunctionalExtensions;
using EducationContentService.Domain.Projects.ValueObjects;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Unit.Projects;

public sealed class UrlTests
{
    [Theory]
    [InlineData("https://example.com/resource")]
    [InlineData("http://localhost:3000/resource")]
    [InlineData("HTTPS://EXAMPLE.COM/resource")]
    public void Create_AllowsHttpUrlsWithHost(string value)
    {
        Result<Url, Error> result = Url.Create(value);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//example.com/resource")]
    [InlineData("https:resource")]
    public void Create_RejectsUnsafeOrHostlessUrls(string value)
    {
        Result<Url, Error> result = Url.Create(value);

        Assert.True(result.IsFailure);
    }
}