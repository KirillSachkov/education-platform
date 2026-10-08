using CSharpFunctionalExtensions;
using SharedKernel;

namespace Common.Tests;

public sealed class EntityReferenceTests
{
    [Fact]
    public void Of_WithValidId_ReturnsSuccess()
    {
        Guid id = Guid.CreateVersion7();

        var result = TestEntityReference.Of(EntityType.Material, id);

        Assert.True(result.IsSuccess);
        Assert.Equal(EntityType.Material, result.Value.Type);
        Assert.Equal(id, result.Value.Id);
    }

    [Fact]
    public void Of_WithEmptyId_ReturnsFailure()
    {
        var result = TestEntityReference.Of(EntityType.Material, Guid.Empty);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Of_WithUndefinedEntityType_ReturnsFailure()
    {
        var result = TestEntityReference.Of((EntityType)999, Guid.CreateVersion7());

        Assert.True(result.IsFailure);
    }

    private sealed record TestEntityReference : EntityReference
    {
        private TestEntityReference(EntityType type, Guid id)
            : base(type, id)
        {
        }

        public static Result<TestEntityReference, Error> Of(EntityType type, Guid id)
        {
            UnitResult<Error> validationResult = ValidateBase(type, id);
            if (validationResult.IsFailure)
            {
                return validationResult.Error;
            }

            return new TestEntityReference(type, id);
        }
    }
}
