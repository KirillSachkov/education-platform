using AssignmentReviewService.Domain.AiSettings;
using AssignmentReviewService.Core.Features.Reviews.UseCases;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AssignmentReviewService.UnitTests.Configuration;

public sealed class AiModelSlotTests
{
    [Theory]
    [InlineData(100_001, 300)]
    [InlineData(4_000, 3_601)]
    public void Resource_limits_above_safe_bounds_should_be_rejected(
        int maxOutputTokens,
        int timeoutSeconds)
    {
        Result<AiModelSlot, Error> result = AiModelSlot.Create(
            "deepseek/deepseek-v4-pro",
            0.2,
            maxOutputTokens,
            timeoutSeconds);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Model_override_above_provider_model_limit_should_be_rejected()
    {
        string oversized = new('m', AiModelSlot.MAX_MODEL_LENGTH + 1);

        FluentValidation.Results.ValidationResult request =
            await new RequestRunIterationValidator().ValidateAsync(
                new RequestRunIterationCommand(Guid.CreateVersion7(), oversized));
        FluentValidation.Results.ValidationResult worker =
            await new RunIterationValidator().ValidateAsync(
                new RunIterationCommand(Guid.CreateVersion7(), oversized));

        Assert.False(request.IsValid);
        Assert.False(worker.IsValid);
    }
}
