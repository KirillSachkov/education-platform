namespace AccessService.Domain.Onboarding;

/// <summary>
///     Aggregate root: состояние прохождения онбординга пользователем по конкретному плану.
///     Один раз создаётся при первом получении plan-grant'а на план с `flow.is_enabled=true`,
///     никогда не пере-создаётся (даже после revoke + повторный grant).
/// </summary>
public sealed class UserPlanOnboarding
{
    private readonly List<Guid> _skippedStepIds = [];
    private readonly List<Guid> _completedStepIds = [];

    private UserPlanOnboarding() { } // EF

    private UserPlanOnboarding(
        Guid userId,
        Guid planId,
        DateTimeOffset startedAt)
    {
        UserId = userId;
        PlanId = planId;
        StartedAt = startedAt;
    }

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? CurrentStepId { get; private set; }

    public IReadOnlyList<Guid> SkippedStepIds => _skippedStepIds;

    public IReadOnlyList<Guid> CompletedStepIds => _completedStepIds;

    public bool IsCompleted => CompletedAt.HasValue;

    public static UserPlanOnboarding Start(Guid userId, Guid planId, DateTimeOffset now) =>
        new(userId, planId, now);

    public void SetCurrentStep(Guid? stepId) => CurrentStepId = stepId;

    /// <summary>
    ///     Переместить курсор на конкретный шаг (для кнопки «Назад» в wizard).
    ///     Не сбрасывает <c>completed</c>/<c>skipped</c> пометки — юзер просто видит
    ///     указанный шаг как «текущий», но если он его уже прошёл — история сохраняется.
    /// </summary>
    public UnitResult<Error> ReturnToStep(Guid stepId)
    {
        if (IsCompleted) return OnboardingErrors.OnboardingAlreadyCompleted();
        CurrentStepId = stepId;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Перемещает <see cref="CurrentStepId"/> на ближайший pending-шаг
    ///     в порядке <paramref name="orderedStepIds"/>. Если все шаги пройдены
    ///     или пропущены — устанавливает <c>null</c> (UI покажет CompletionView).
    /// </summary>
    public void AdvanceTo(IEnumerable<Guid> orderedStepIds)
    {
        Guid? next = orderedStepIds
            .Where(id => !_skippedStepIds.Contains(id) && !_completedStepIds.Contains(id))
            .Cast<Guid?>()
            .FirstOrDefault();
        CurrentStepId = next;
    }

    public UnitResult<Error> SkipStep(Guid stepId)
    {
        if (IsCompleted) return OnboardingErrors.OnboardingAlreadyCompleted();
        if (_skippedStepIds.Contains(stepId) || _completedStepIds.Contains(stepId))
        {
            return UnitResult.Success<Error>();
        }

        _skippedStepIds.Add(stepId);
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> CompleteStep(Guid stepId)
    {
        if (IsCompleted) return OnboardingErrors.OnboardingAlreadyCompleted();
        if (_completedStepIds.Contains(stepId))
        {
            return UnitResult.Success<Error>();
        }

        _skippedStepIds.Remove(stepId); // если был skipped — переводим в completed
        _completedStepIds.Add(stepId);
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Сброс прогресса — пользователь хочет «пройти онбординг заново».
    ///     Очищает completed/skipped списки, обнуляет CompletedAt, выставляет
    ///     CurrentStepId на первый шаг flow.
    /// </summary>
    public void Reset(IEnumerable<Guid> orderedStepIds, DateTimeOffset now)
    {
        _completedStepIds.Clear();
        _skippedStepIds.Clear();
        CompletedAt = null;
        StartedAt = now;
        Guid? first = orderedStepIds.Cast<Guid?>().FirstOrDefault();
        CurrentStepId = first;
    }

    public UnitResult<Error> Complete(IReadOnlyList<Guid> allStepIds, DateTimeOffset now)
    {
        if (IsCompleted) return UnitResult.Success<Error>();

        // Все шаги должны быть либо в skipped, либо в completed.
        foreach (Guid id in allStepIds)
        {
            if (!_skippedStepIds.Contains(id) && !_completedStepIds.Contains(id))
            {
                return OnboardingErrors.OnboardingHasPendingSteps();
            }
        }

        CompletedAt = now;
        CurrentStepId = null;
        return UnitResult.Success<Error>();
    }
}
