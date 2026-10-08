using ProgressService.Domain.Modules;
using ProgressService.Domain.Modules.Events;

namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

public class ModuleProgressTests
{
    [Fact]
    public void MarkFirstItemCompleted_SetsInProgress()
    {
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 2).Value;

        var result = progress.MarkItemCompleted();

        Assert.True(result.IsSuccess);
        Assert.Equal(ModuleProgressStatus.IN_PROGRESS, progress.Status);
        Assert.Equal(1, progress.ItemsCompleted);
    }

    [Fact]
    public void TryCompleteModule_WhenAllItemsCompleted_Succeeds()
    {
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 2).Value;
        progress.MarkItemCompleted();
        progress.MarkItemCompleted();

        var result = progress.TryCompleteModule();

        Assert.True(result.IsSuccess);
        Assert.Equal(ModuleProgressStatus.COMPLETED, progress.Status);
        Assert.NotNull(progress.CompletedAt);
        Assert.Single(progress.DomainEvents);
        Assert.IsType<ModuleProgressCompletedEvent>(progress.DomainEvents[0]);
    }

    [Fact]
    public void TryCompleteModule_BeforeAllItemsCompleted_DoesNothing()
    {
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 2).Value;
        progress.MarkItemCompleted();

        var result = progress.TryCompleteModule();

        Assert.True(result.IsSuccess);
        Assert.Equal(ModuleProgressStatus.IN_PROGRESS, progress.Status);
        Assert.Null(progress.CompletedAt);
    }

    [Fact]
    public void TryCompleteModule_WhenCalledAfterCompletion_DoesNotRaiseSecondEvent()
    {
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 1).Value;
        progress.MarkItemCompleted();
        progress.TryCompleteModule();

        var result = progress.TryCompleteModule();

        Assert.True(result.IsSuccess);
        Assert.Single(progress.DomainEvents);
    }

    [Fact]
    public void MarkItemCompleted_WhenCounterFull_AutoGrowsItemsTotal()
    {
        // Regression for issue #199: ItemsTotal в ModuleProgress фиксируется при создании
        // и не реактивно синкается на attach/detach module items в ECS. Если автор удалил
        // уже завершённый item, а потом добавил новый — counter может выйти за старый
        // total. MarkItemCompleted должен self-heal'иться (растить total), а не падать
        // с CounterCannotExceedTotal, иначе approve issue blocking flow ломается.
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 2).Value;
        progress.MarkItemCompleted();
        progress.MarkItemCompleted();
        progress.TryCompleteModule();

        var result = progress.MarkItemCompleted();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, progress.ItemsCompleted);
        Assert.Equal(3, progress.ItemsTotal);
        Assert.Equal(ModuleProgressStatus.COMPLETED, progress.Status);
    }

    [Fact]
    public void SyncItemsTotal_WhenLarger_UpdatesTotalKeepsCompleted()
    {
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 3).Value;
        progress.MarkItemCompleted();
        progress.MarkItemCompleted();

        bool changed = progress.SyncItemsTotal(5);

        Assert.True(changed);
        Assert.Equal(5, progress.ItemsTotal);
        Assert.Equal(2, progress.ItemsCompleted);
    }

    [Fact]
    public void SyncItemsTotal_WhenSmallerThanCompleted_ClampsCompleted()
    {
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 4).Value;
        progress.MarkItemCompleted();
        progress.MarkItemCompleted();
        progress.MarkItemCompleted();

        bool changed = progress.SyncItemsTotal(2);

        Assert.True(changed);
        Assert.Equal(2, progress.ItemsTotal);
        Assert.Equal(2, progress.ItemsCompleted);
    }

    [Fact]
    public void SyncItemsTotal_WhenUnchanged_ReturnsFalse()
    {
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 3).Value;

        bool changed = progress.SyncItemsTotal(3);

        Assert.False(changed);
        Assert.Equal(3, progress.ItemsTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SyncItemsTotal_WhenNonPositive_ReturnsFalse(int newTotal)
    {
        // Guard: empty/negative module — нештатная ситуация, sync игнорируется,
        // чтобы не оставить (ItemsTotal=0, ItemsCompleted=0, Status=COMPLETED).
        ModuleProgress progress = ModuleProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 3).Value;
        progress.MarkItemCompleted();

        bool changed = progress.SyncItemsTotal(newTotal);

        Assert.False(changed);
        Assert.Equal(3, progress.ItemsTotal);
        Assert.Equal(1, progress.ItemsCompleted);
    }
}
