using Ordering;

namespace AccessService.Domain.Onboarding;

/// <summary>
///     Aggregate root: онбординг плана. 1:1 с Plan (по PlanId).
///     Контейнер шагов + флаг is_enabled. Авто-шаги управляются через
///     EnsureAutoStep / RemoveAutoStep, MARKDOWN — через AddMarkdownStep / Update / Remove.
/// </summary>
public sealed class PlanOnboardingFlow
{
    private readonly List<PlanOnboardingStep> _steps = [];

    private PlanOnboardingFlow() { } // EF

    private PlanOnboardingFlow(Guid planId, bool isEnabled, DateTimeOffset createdAt)
    {
        PlanId = planId;
        IsEnabled = isEnabled;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid PlanId { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<PlanOnboardingStep> Steps => _steps;

    /// <summary>
    ///     Создаёт пустой flow для плана. По умолчанию выключен (явный opt-in автора).
    /// </summary>
    public static PlanOnboardingFlow Create(Guid planId, DateTimeOffset now) =>
        new(planId, isEnabled: false, now);

    public void Enable(DateTimeOffset now)
    {
        if (IsEnabled) return;
        IsEnabled = true;
        UpdatedAt = now;
    }

    public void Disable(DateTimeOffset now)
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        UpdatedAt = now;
    }

    public Result<PlanOnboardingStep, Error> AddMarkdownStep(
        string title,
        string body,
        bool isSkippable,
        DateTimeOffset now)
    {
        SortKey order = _steps.Count == 0
            ? SortKey.Initial()
            : SortKey.After(LastSortOrder());

        Result<PlanOnboardingStep, Error> step = PlanOnboardingStep.CreateMarkdown(
            PlanId, title, body, isSkippable, order, now);
        if (step.IsFailure) return step.Error;

        _steps.Add(step.Value);
        UpdatedAt = now;
        return step.Value;
    }

    public UnitResult<Error> UpdateMarkdownStep(
        Guid stepId,
        string title,
        string body,
        bool isSkippable,
        DateTimeOffset now)
    {
        PlanOnboardingStep? step = _steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null) return OnboardingErrors.StepNotFound();

        UnitResult<Error> update = step.UpdateMarkdown(title, body, isSkippable, now);
        if (update.IsFailure) return update.Error;

        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> RemoveStep(Guid stepId, DateTimeOffset now)
    {
        PlanOnboardingStep? step = _steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null) return OnboardingErrors.StepNotFound();

        if (step.Type != PlanOnboardingStepType.MARKDOWN)
        {
            return OnboardingErrors.AutoStepNotRemovable();
        }

        _steps.Remove(step);
        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> SetStepIsSkippable(Guid stepId, bool isSkippable, DateTimeOffset now)
    {
        PlanOnboardingStep? step = _steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null) return OnboardingErrors.StepNotFound();

        step.SetIsSkippable(isSkippable, now);
        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> ReorderStep(Guid stepId, Guid? beforeId, Guid? afterId, DateTimeOffset now)
    {
        PlanOnboardingStep? step = _steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null) return OnboardingErrors.StepNotFound();

        SortKey? before = null;
        SortKey? after = null;

        if (beforeId.HasValue)
        {
            PlanOnboardingStep? b = _steps.FirstOrDefault(s => s.Id == beforeId.Value);
            if (b is null) return OnboardingErrors.StepNotFound();
            before = b.SortOrder;
        }

        if (afterId.HasValue)
        {
            PlanOnboardingStep? a = _steps.FirstOrDefault(s => s.Id == afterId.Value);
            if (a is null) return OnboardingErrors.StepNotFound();
            after = a.SortOrder;
        }

        Result<SortKey, Error> newKey = SortKey.Between(before, after);
        if (newKey.IsFailure) return newKey.Error;

        step.UpdateOrder(newKey.Value, now);
        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Идемпотентно добавляет авто-шаг (TG/GH/NOTIF) если его ещё нет.
    /// </summary>
    public PlanOnboardingStep EnsureAutoStep(PlanOnboardingStepType type, DateTimeOffset now)
    {
        if (type == PlanOnboardingStepType.MARKDOWN)
        {
            throw new ArgumentException("MARKDOWN — only via AddMarkdownStep.", nameof(type));
        }

        PlanOnboardingStep? existing = _steps.FirstOrDefault(s => s.Type == type);
        if (existing is not null) return existing;

        SortKey order = _steps.Count == 0
            ? SortKey.Initial()
            : SortKey.After(LastSortOrder());

        PlanOnboardingStep step = PlanOnboardingStep.CreateAuto(PlanId, type, order, now);
        _steps.Add(step);
        UpdatedAt = now;
        return step;
    }

    /// <summary>
    ///     Идемпотентно убирает авто-шаг (TG/GH). NOTIFICATIONS не убирается:
    ///     если flow включён — он там есть, если выключен — flow весь скрыт.
    ///     Domain invariant: NOTIFICATIONS-шаг существует пока IsEnabled=true.
    /// </summary>
    public void RemoveAutoStep(PlanOnboardingStepType type, DateTimeOffset now)
    {
        if (type == PlanOnboardingStepType.MARKDOWN)
        {
            throw new ArgumentException("MARKDOWN — only via RemoveStep.", nameof(type));
        }

        if (type == PlanOnboardingStepType.NOTIFICATIONS)
        {
            throw new ArgumentException(
                "NOTIFICATIONS step is invariant of an enabled flow and cannot be removed.",
                nameof(type));
        }

        PlanOnboardingStep? existing = _steps.FirstOrDefault(s => s.Type == type);
        if (existing is null) return;

        _steps.Remove(existing);
        UpdatedAt = now;
    }

    private SortKey LastSortOrder() =>
        _steps.OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal).Last().SortOrder;
}
