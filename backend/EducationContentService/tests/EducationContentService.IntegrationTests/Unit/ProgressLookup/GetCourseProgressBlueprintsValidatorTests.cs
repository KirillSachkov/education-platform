using EducationContentService.Core.Features.ProgressLookup;
using FluentValidation.Results;

namespace EducationContentService.IntegrationTests.Unit.ProgressLookup;

public sealed class GetCourseProgressBlueprintsValidatorTests
{
    private readonly GetCourseProgressBlueprintsQueryValidator _validator = new();

    [Fact]
    public void Validate_AllowsEmptyBatch()
    {
        ValidationResult result = _validator.Validate(new GetCourseProgressBlueprintsQuery([]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AllowsTwoHundredUniqueIds()
    {
        Guid[] ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();

        ValidationResult result = _validator.Validate(new GetCourseProgressBlueprintsQuery(ids));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsTwoHundredOneIds()
    {
        Guid[] ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();

        ValidationResult result = _validator.Validate(new GetCourseProgressBlueprintsQuery(ids));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        Guid id = Guid.NewGuid();

        ValidationResult result = _validator.Validate(new GetCourseProgressBlueprintsQuery([id, id]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsEmptyGuid()
    {
        ValidationResult result = _validator.Validate(new GetCourseProgressBlueprintsQuery([Guid.Empty]));

        Assert.False(result.IsValid);
    }
}