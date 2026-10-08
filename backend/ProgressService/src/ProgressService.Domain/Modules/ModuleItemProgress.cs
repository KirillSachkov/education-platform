namespace ProgressService.Domain.Modules;

/// <summary>
/// Маркер завершения конкретного элемента модуля.
/// Используется как общий слой между item-specific логикой и агрегированным завершением модуля.
/// </summary>
public sealed class ModuleItemProgress
{
    private ModuleItemProgress(
        Guid enrollmentId,
        Guid moduleId,
        Guid referenceId,
        ModuleItemProgressType itemType)
    {
        Id = Guid.CreateVersion7();
        EnrollmentId = enrollmentId;
        ModuleId = moduleId;
        ReferenceId = referenceId;
        ItemType = itemType;
        Status = ModuleItemProgressStatus.NOT_COMPLETED;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private ModuleItemProgress()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid EnrollmentId { get; private set; }

    public Guid ModuleId { get; private set; }

    public Guid ReferenceId { get; private set; }

    public ModuleItemProgressType ItemType { get; private set; }

    public ModuleItemProgressStatus Status { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<ModuleItemProgress, Error> Create(
        Guid enrollmentId,
        Guid moduleId,
        Guid referenceId,
        ModuleItemProgressType itemType)
    {
        if (enrollmentId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(enrollmentId));
        }

        if (moduleId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(moduleId));
        }

        if (referenceId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(referenceId));
        }

        return new ModuleItemProgress(enrollmentId, moduleId, referenceId, itemType);
    }

    public static Result<ModuleItemProgress, Error> CreateIssueProgress(
        Guid enrollmentId,
        Guid moduleId,
        Guid issueId) =>
        Create(enrollmentId, moduleId, issueId, ModuleItemProgressType.ISSUE);

    public static Result<ModuleItemProgress, Error> CreateMaterialProgress(
        Guid enrollmentId,
        Guid moduleId,
        Guid materialId) =>
        Create(enrollmentId, moduleId, materialId, ModuleItemProgressType.MATERIAL);

    public static Result<ModuleItemProgress, Error> CreateQuizProgress(
        Guid enrollmentId,
        Guid moduleId,
        Guid quizId) =>
        Create(enrollmentId, moduleId, quizId, ModuleItemProgressType.QUIZ);

    public Result<bool, Error> MarkCompleted()
    {
        if (Status == ModuleItemProgressStatus.COMPLETED)
        {
            return false;
        }

        Status = ModuleItemProgressStatus.COMPLETED;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = CompletedAt.Value;

        return true;
    }

    /// <summary>
    /// Снимает отметку завершённости. Возвращает true, если статус действительно изменился
    /// (из COMPLETED в NOT_COMPLETED), и false, если он уже был не завершён. Используется
    /// при reopen ревью задачи.
    /// </summary>
    public bool MarkUncompleted()
    {
        if (Status != ModuleItemProgressStatus.COMPLETED)
        {
            return false;
        }

        Status = ModuleItemProgressStatus.NOT_COMPLETED;
        CompletedAt = null;
        UpdatedAt = DateTime.UtcNow;

        return true;
    }
}
