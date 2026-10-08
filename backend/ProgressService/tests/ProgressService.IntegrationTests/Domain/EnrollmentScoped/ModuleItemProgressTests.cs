using ProgressService.Domain.Modules;

namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

public class ModuleItemProgressTests
{
    [Theory]
    [InlineData(ModuleItemProgressType.MATERIAL)]
    [InlineData(ModuleItemProgressType.ISSUE)]
    public void MarkCompleted_IsIdempotent_ForSupportedTypes(ModuleItemProgressType itemType)
    {
        ModuleItemProgress progress = ModuleItemProgress.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            itemType).Value;

        var first = progress.MarkCompleted();
        var second = progress.MarkCompleted();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, progress.Status);
        Assert.NotNull(progress.CompletedAt);
    }
}
