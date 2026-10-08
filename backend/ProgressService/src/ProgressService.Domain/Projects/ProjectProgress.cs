using ProgressService.Domain.Projects.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Projects;

/// <summary>
/// Агрегированный снимок прогресса пользователя по проекту в рамках конкретного enrollment.
/// Отслеживает количество завершенных задач и определяет момент полного завершения проекта.
/// </summary>
public sealed class ProjectProgress : AggregateRoot
{
    private ProjectProgress(Guid enrollmentId, Guid projectId, int totalIssuesCount)
    {
        Id = Guid.CreateVersion7();
        EnrollmentId = enrollmentId;
        ProjectId = projectId;
        Status = ProjectProgressStatus.IN_PROGRESS;
        TotalIssuesCount = totalIssuesCount;
        TotalIssuesCompleted = 0;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private ProjectProgress()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid EnrollmentId { get; private set; }

    public Guid ProjectId { get; private set; }

    public ProjectProgressStatus Status { get; private set; }

    public int TotalIssuesCount { get; private set; }

    public int TotalIssuesCompleted { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<ProjectProgress, Error> Create(Guid enrollmentId, Guid projectId, int totalIssuesCount)
    {
        if (enrollmentId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(enrollmentId));
        }

        if (projectId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(projectId));
        }

        if (totalIssuesCount < 0)
        {
            return ProgressErrors.CounterCannotBeNegative(nameof(TotalIssuesCount));
        }

        return new ProjectProgress(enrollmentId, projectId, totalIssuesCount);
    }

    /// <summary>
    /// Реакция на добавление новой задачи в проект (issue.published в ECS).
    /// Бампит TotalIssuesCount и откатывает COMPLETED → IN_PROGRESS, если проект уже был завершён.
    /// XP за повторное завершение не начисляется повторно — XpAwardService идемпотентен по SourceId.
    /// </summary>
    public void RegisterIssueAdded()
    {
        TotalIssuesCount++;

        if (Status == ProjectProgressStatus.COMPLETED)
        {
            Status = ProjectProgressStatus.IN_PROGRESS;
            CompletedAt = null;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public UnitResult<Error> RegisterIssueCompleted()
    {
        if (TotalIssuesCompleted >= TotalIssuesCount)
        {
            return ProgressErrors.CounterCannotExceedTotal(
                nameof(TotalIssuesCompleted),
                nameof(TotalIssuesCount));
        }

        TotalIssuesCompleted++;

        if (Status == ProjectProgressStatus.NOT_STARTED)
        {
            Status = ProjectProgressStatus.IN_PROGRESS;
        }

        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> TryCompleteProject()
    {
        if (Status == ProjectProgressStatus.COMPLETED)
        {
            return UnitResult.Success<Error>();
        }

        if (TotalIssuesCount <= 0 || TotalIssuesCompleted != TotalIssuesCount)
        {
            return UnitResult.Success<Error>();
        }

        Status = ProjectProgressStatus.COMPLETED;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;
        RaiseDomainEvent(new ProjectProgressCompletedEvent(this));

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Откатывает счётчик завершённых задач на 1. Если проект был COMPLETED — возвращает его
    /// в IN_PROGRESS и обнуляет <see cref="CompletedAt"/>. Используется при reopen ревью.
    /// </summary>
    public UnitResult<Error> RegisterIssueUncompleted()
    {
        if (TotalIssuesCompleted <= 0)
        {
            return ProgressErrors.CounterCannotBeNegative(nameof(TotalIssuesCompleted));
        }

        TotalIssuesCompleted--;

        if (Status == ProjectProgressStatus.COMPLETED)
        {
            Status = ProjectProgressStatus.IN_PROGRESS;
            CompletedAt = null;
        }

        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }
}
