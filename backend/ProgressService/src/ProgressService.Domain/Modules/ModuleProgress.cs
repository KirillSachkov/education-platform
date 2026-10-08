using ProgressService.Domain.Modules.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Modules;

/// <summary>
/// Агрегированный снимок прогресса пользователя по модулю в рамках конкретного enrollment.
/// Отслеживает количество завершенных элементов модуля и момент завершения всего модуля.
/// </summary>
public sealed class ModuleProgress : AggregateRoot
{
    private ModuleProgress(Guid enrollmentId, Guid moduleId, int itemsTotal)
    {
        Id = Guid.CreateVersion7();
        EnrollmentId = enrollmentId;
        ModuleId = moduleId;
        Status = ModuleProgressStatus.IN_PROGRESS;
        ItemsTotal = itemsTotal;
        ItemsCompleted = 0;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private ModuleProgress()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; init; }

    public Guid EnrollmentId { get; private set; }

    public Guid ModuleId { get; private set; }

    public ModuleProgressStatus Status { get; private set; }

    public int ItemsTotal { get; private set; }

    public int ItemsCompleted { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<ModuleProgress, Error> Create(Guid enrollmentId, Guid moduleId, int itemsTotal)
    {
        if (enrollmentId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(enrollmentId));
        }

        if (moduleId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(moduleId));
        }

        if (itemsTotal < 0)
        {
            return ProgressErrors.CounterCannotBeNegative(nameof(ItemsTotal));
        }

        return new ModuleProgress(enrollmentId, moduleId, itemsTotal);
    }

    public UnitResult<Error> MarkItemCompleted()
    {
        ItemsCompleted++;

        // Self-heal: ECS module structure (count of items) is the source of truth, но
        // ItemsTotal в ModuleProgress снимается лишь раз при создании и не реактивно
        // синкается на attach/detach/move events. Если автор удалил уже завершённый
        // item, а потом добавил новый — counter может выйти за старый total. Растим
        // total чтобы не залочить approve/view flow. Real reconciliation идёт через
        // EnsureModuleProgressAsync (issue #199).
        if (ItemsCompleted > ItemsTotal)
        {
            ItemsTotal = ItemsCompleted;
        }

        if (Status == ModuleProgressStatus.NOT_STARTED)
        {
            Status = ModuleProgressStatus.IN_PROGRESS;
        }

        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Синхронизирует <see cref="ItemsTotal"/> с актуальным числом items в модуле из ECS.
    /// Вызывается из <c>EnsureModuleProgressAsync</c> при user-driven действиях
    /// (StartIssueWork, MarkMaterialViewed) — фоновая reconciliation структуры модуля.
    /// При уменьшении total <see cref="ItemsCompleted"/> клампится до нового значения, чтобы
    /// сохранить инвариант <c>ItemsCompleted &lt;= ItemsTotal</c>. <paramref name="actualItemsTotal"/>
    /// ≤ 0 игнорируется (пустой модуль — нештатная ситуация, ECS должен либо удалить
    /// ModuleProgress, либо вернуть items; иначе оставили бы COMPLETED/0/0 inconsistent state).
    /// Status не трогается — на approve пути <c>MarkItemCompleted</c> сам нарастит total, на
    /// student-пути модуль ещё попадёт в <c>TryCompleteModule</c>.
    /// </summary>
    public bool SyncItemsTotal(int actualItemsTotal)
    {
        if (actualItemsTotal <= 0 || actualItemsTotal == ItemsTotal)
        {
            return false;
        }

        ItemsTotal = actualItemsTotal;

        if (ItemsCompleted > ItemsTotal)
        {
            ItemsCompleted = ItemsTotal;
        }

        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public UnitResult<Error> TryCompleteModule()
    {
        if (Status == ModuleProgressStatus.COMPLETED || ItemsTotal <= 0 || ItemsCompleted != ItemsTotal)
        {
            return UnitResult.Success<Error>();
        }

        Status = ModuleProgressStatus.COMPLETED;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
        RaiseDomainEvent(new ModuleProgressCompletedEvent(this));

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Откатывает счётчик завершённых элементов на 1. Если модуль был COMPLETED — возвращает
    /// его в IN_PROGRESS и обнуляет <see cref="CompletedAt"/>. Используется при reopen ревью задачи.
    /// </summary>
    public UnitResult<Error> MarkItemUncompleted()
    {
        if (ItemsCompleted <= 0)
        {
            return ProgressErrors.CounterCannotBeNegative(nameof(ItemsCompleted));
        }

        ItemsCompleted--;

        if (Status == ModuleProgressStatus.COMPLETED)
        {
            Status = ModuleProgressStatus.IN_PROGRESS;
            CompletedAt = null;
        }

        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }
}
