using EducationContentService.Contracts.Issues;
using EducationContentService.Core.Features.ProjectItems.UseCases;
using FluentValidation.Results;

namespace EducationContentService.IntegrationTests.Unit.ProjectItems;

public sealed class UpdateIssueInternalMaterialsValidatorTests
{
    private readonly UpdateIssueInternalMaterialsRequestValidator _validator = new();

    [Fact]
    public void Validate_AllowsEmptyListToClearReferences()
    {
        ValidationResult result = _validator.Validate(new UpdateIssueInternalMaterialsRequest([]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AllowsFiftyUniqueMaterials()
    {
        var request = new UpdateIssueInternalMaterialsRequest(
            Enumerable.Range(0, 50)
                .Select(_ => new InternalMaterialItem("Material", Guid.NewGuid(), false))
                .ToArray());

        ValidationResult result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsFiftyOneMaterials()
    {
        var request = new UpdateIssueInternalMaterialsRequest(
            Enumerable.Range(0, 51)
                .Select(_ => new InternalMaterialItem("Material", Guid.NewGuid(), false))
                .ToArray());

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsNullItems()
    {
        var request = new UpdateIssueInternalMaterialsRequest(null!);

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsDuplicateReferences()
    {
        Guid id = Guid.NewGuid();
        var request = new UpdateIssueInternalMaterialsRequest(
        [
            new InternalMaterialItem("Material", id, false),
            new InternalMaterialItem("Material", id, true),
        ]);

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("Quiz")]
    [InlineData("Issue")]
    [InlineData("999")]
    [InlineData("")]
    public void Validate_RejectsNonMaterialItemTypes(string itemType)
    {
        var request = new UpdateIssueInternalMaterialsRequest(
            [new InternalMaterialItem(itemType, Guid.NewGuid(), false)]);

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsEmptyReferenceId()
    {
        var request = new UpdateIssueInternalMaterialsRequest(
            [new InternalMaterialItem("Material", Guid.Empty, false)]);

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }
}